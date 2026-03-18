using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StoryFlow.Data;
using UnityEditor;
using UnityEngine;

namespace StoryFlow.Editor
{
    /// <summary>
    /// WebSocket client that connects to StoryFlow Editor for live sync.
    /// Receives JSON updates when scripts change in the editor.
    /// Accessible via menu: StoryFlow > Live Sync.
    ///
    /// Protocol:
    ///   After connecting, sends a "connect" message identifying this client as Unity.
    ///   The editor broadcasts "project-updated" after exporting JSON to the build/ directory.
    ///   This client reads the build directory and re-imports using StoryFlowImporter.
    ///   The client can also send "request-sync" to ask the editor to re-export.
    /// </summary>
    public class StoryFlowLiveSyncServer : EditorWindow
    {
        [MenuItem("StoryFlow/Live Sync")]
        private static void ShowWindow()
        {
            var window = GetWindow<StoryFlowLiveSyncServer>("StoryFlow Live Sync");
            window.minSize = new Vector2(350, 300);
        }

        // --- Settings ---
        private const string host = "localhost";
        private int port = 9000;
        private Texture2D logoTexture;
        private string outputPath = "Assets/StoryFlow";

        // --- State ---
        private bool isConnected;
        private ClientWebSocket webSocket;
        private CancellationTokenSource cts;
        private string statusMessage = "Disconnected";
        private bool isImporting;

        // --- Queued actions (messages arrive on background thread, Unity APIs need main thread) ---
        private readonly Queue<Action> mainThreadQueue = new();
        private readonly object queueLock = new object();

        // --- Log ---
        private readonly List<string> logMessages = new();
        private const int MaxLogMessages = 100;
        private Vector2 logScroll;

        // --- Async task tracking ---
        private Task receiveTask;

        // --- Plugin version for identification ---
        private const string PluginVersion = "1.0.0";

        // --- EditorPrefs keys for auto-reconnect across domain reloads ---
        private const string PrefKeyWasConnected = "StoryFlow_LiveSync_WasConnected";
        private const string PrefKeyPort = "StoryFlow_LiveSync_Port";

        private void OnEnable()
        {
            EditorApplication.update += EditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // Load logo
            logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Packages/com.storyflow.unity/Editor/Resources/storyflow_logo.png");

            // Auto-reconnect if we were connected before domain reload
            if (EditorPrefs.GetBool(PrefKeyWasConnected, false))
            {
                EditorPrefs.DeleteKey(PrefKeyWasConnected);
                port = EditorPrefs.GetInt(PrefKeyPort, 9000);
                Connect();
            }
        }

        private void OnDisable()
        {
            EditorApplication.update -= EditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Disconnect();
        }

        private void OnDestroy()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Disconnect();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode && isConnected)
            {
                // Save connection state before domain reload
                EditorPrefs.SetBool(PrefKeyWasConnected, true);
                EditorPrefs.SetInt(PrefKeyPort, port);
            }
        }

        /// <summary>
        /// Polled every editor frame to process queued actions and check task health.
        /// WebSocket callbacks arrive on background threads, so we queue actions
        /// and execute them here on the main thread.
        /// </summary>
        private void EditorUpdate()
        {
            // Process queued main-thread actions
            while (true)
            {
                Action action;
                lock (queueLock)
                {
                    if (mainThreadQueue.Count == 0) break;
                    action = mainThreadQueue.Dequeue();
                }

                try
                {
                    action?.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    AddLog($"Error in queued action: {ex.Message}");
                }
            }

            // Check if the receive task faulted
            if (receiveTask != null && receiveTask.IsCompleted)
            {
                if (receiveTask.IsFaulted)
                {
                    var ex = receiveTask.Exception?.InnerException ?? receiveTask.Exception;
                    AddLog($"Receive error: {ex?.Message}");
                }

                if (isConnected)
                {
                    isConnected = false;
                    statusMessage = "Disconnected (connection lost)";
                    AddLog("Connection lost.");
                    EditorPrefs.SetBool("StoryFlow_Toolbar_Connected", false);
                    Repaint();
                }

                receiveTask = null;
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);

            // --- Header with logo ---
            EditorGUILayout.BeginHorizontal();
            if (logoTexture != null)
            {
                GUILayout.Label(new GUIContent(logoTexture), GUILayout.Width(24), GUILayout.Height(24));
            }
            EditorGUILayout.LabelField("StoryFlow Live Sync", EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);

            // --- Port setting ---
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.LabelField("Port", GUILayout.Width(30));
                port = EditorGUILayout.IntField(port, GUILayout.Width(60));
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // --- Output path ---
            EditorGUILayout.LabelField("Output Path", EditorStyles.label);
            outputPath = EditorGUILayout.TextField(outputPath);

            EditorGUILayout.Space(4);

            // --- Status ---
            var statusColor = isConnected ? Color.green : new Color(0.8f, 0.4f, 0.1f);
            var originalColor = GUI.color;
            GUI.color = statusColor;
            EditorGUILayout.LabelField($"Status: {statusMessage}");
            GUI.color = originalColor;

            EditorGUILayout.Space(4);

            // --- Connect / Disconnect buttons ---
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUI.BeginDisabledGroup(isConnected);
                if (GUILayout.Button("Connect", GUILayout.Height(28)))
                {
                    Connect();
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!isConnected);
                if (GUILayout.Button("Disconnect", GUILayout.Height(28)))
                {
                    Disconnect();
                }
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();

            // --- Request Sync button ---
            EditorGUI.BeginDisabledGroup(!isConnected || isImporting);
            if (GUILayout.Button(isImporting ? "Importing..." : "Request Sync", GUILayout.Height(28)))
            {
                SendRequestSync();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(8);

            // --- Log ---
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);

            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.ExpandHeight(true));
            {
                foreach (var msg in logMessages)
                {
                    EditorGUILayout.LabelField(msg, EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();

            // --- Clear log ---
            if (GUILayout.Button("Clear Log"))
            {
                logMessages.Clear();
            }
        }

        // ================================================================
        // Connection
        // ================================================================

        private async void Connect()
        {
            try
            {
                if (isConnected) return;

                Disconnect(); // Clean up any previous state

                cts = new CancellationTokenSource();
                webSocket = new ClientWebSocket();

                string uri = $"ws://{host}:{port}";
                statusMessage = $"Connecting to {uri}...";
                AddLog($"Connecting to {uri}...");
                Repaint();

                try
                {
                    await webSocket.ConnectAsync(new Uri(uri), cts.Token);

                    isConnected = true;
                    statusMessage = $"Connected to {uri}";
                    AddLog("Connected.");
                    EditorPrefs.SetBool("StoryFlow_Toolbar_Connected", true);
                    Repaint();

                    // Identify ourselves to the editor
                    SendConnectMessage();

                    // Start receiving messages on a background task
                    receiveTask = ReceiveLoop();
                }
                catch (Exception ex)
                {
                    isConnected = false;
                    statusMessage = $"Connection failed: {ex.Message}";
                    AddLog($"Connection failed: {ex.Message}");
                    Repaint();
                    CleanupWebSocket();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void Disconnect()
        {
            if (cts != null)
            {
                cts.Cancel();
                cts.Dispose();
                cts = null;
            }

            CleanupWebSocket();

            isConnected = false;
            statusMessage = "Disconnected";
            receiveTask = null;
            EditorPrefs.SetBool("StoryFlow_Toolbar_Connected", false);
            Repaint();
        }

        // ================================================================
        // Toolbar Integration
        // ================================================================

        /// <summary>Called by the toolbar button to initiate connection.</summary>
        internal void ConnectFromToolbar()
        {
            if (!isConnected)
                Connect();
        }

        /// <summary>Called by the toolbar button to request a sync.</summary>
        internal void RequestSyncFromToolbar()
        {
            if (isConnected)
                SendRequestSync();
        }

        private void CleanupWebSocket()
        {
            if (webSocket != null)
            {
                try
                {
                    if (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived)
                    {
                        // Fire-and-forget close, don't await in sync context
                        _ = webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                }
                catch
                {
                    // Swallow close errors
                }

                webSocket.Dispose();
                webSocket = null;
            }
        }

        // ================================================================
        // Sending Messages
        // ================================================================

        /// <summary>
        /// Sends a JSON message to the editor over WebSocket.
        /// Safe to call from any thread.
        /// </summary>
        private async void SendMessage(string jsonMessage)
        {
            try
            {
                if (webSocket == null || webSocket.State != WebSocketState.Open) return;

                var bytes = Encoding.UTF8.GetBytes(jsonMessage);
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                    cts?.Token ?? CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                AddLogFromBackground($"Send error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends the "connect" handshake message identifying this client as Unity.
        /// </summary>
        private void SendConnectMessage()
        {
            var msg = new JObject
            {
                ["type"] = "connect",
                ["payload"] = new JObject
                {
                    ["engine"] = "unity",
                    ["version"] = Application.unityVersion,
                    ["pluginVersion"] = PluginVersion
                }
            };
            SendMessage(msg.ToString(Newtonsoft.Json.Formatting.None));
            AddLog("Sent connect handshake.");
        }

        /// <summary>
        /// Sends a "request-sync" message asking the editor to re-export and broadcast.
        /// </summary>
        private void SendRequestSync()
        {
            var msg = new JObject { ["type"] = "request-sync" };
            SendMessage(msg.ToString(Newtonsoft.Json.Formatting.None));
            AddLog("Requested sync from editor.");
        }

        // ================================================================
        // Receive Loop
        // ================================================================

        private async Task ReceiveLoop()
        {
            var buffer = new byte[8192];
            var messageBuilder = new StringBuilder();

            try
            {
                while (webSocket != null &&
                       webSocket.State == WebSocketState.Open &&
                       cts != null && !cts.Token.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    messageBuilder.Clear();

                    do
                    {
                        result = await webSocket.ReceiveAsync(
                            new ArraySegment<byte>(buffer), cts.Token);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            AddLogFromBackground("Server closed the connection.");
                            return;
                        }

                        messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    } while (!result.EndOfMessage);

                    string message = messageBuilder.ToString();
                    ProcessMessage(message);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected on disconnect
            }
            catch (WebSocketException ex)
            {
                AddLogFromBackground($"WebSocket error: {ex.Message}");
            }
            catch (Exception ex)
            {
                AddLogFromBackground($"Receive error: {ex.Message}");
            }
        }

        // ================================================================
        // Message Processing
        // ================================================================

        /// <summary>
        /// Processes an incoming WebSocket message from the StoryFlow Editor.
        /// Called on the background receive thread. Dispatches Unity API work
        /// to the main thread via the queued action system.
        ///
        /// Message format: { "type": "...", "payload": { ... } }
        ///
        /// Supported message types:
        ///   "project-updated" - Editor has exported JSON to the build/ directory.
        ///                       payload.projectPath = absolute path to the project root.
        ///                       The build directory is at {projectPath}/build/.
        ///   "pong"            - Response to a ping (no action needed).
        /// </summary>
        private void ProcessMessage(string message)
        {
            // Truncate for display
            string displayMsg = message.Length > 200
                ? message.Substring(0, 200) + "..."
                : message;

            AddLogFromBackground($"Received: {displayMsg}");

            JObject json;
            try
            {
                json = JObject.Parse(message);
            }
            catch (Exception ex)
            {
                AddLogFromBackground($"Failed to parse message JSON: {ex.Message}");
                return;
            }

            string type = json.Value<string>("type") ?? "";
            JObject payload = json.Value<JObject>("payload");

            switch (type)
            {
                case "project-updated":
                    HandleProjectUpdated(payload);
                    break;

                case "pong":
                    // No action needed — server responded to our ping
                    break;

                default:
                    AddLogFromBackground($"Unknown message type: {type}");
                    break;
            }
        }

        /// <summary>
        /// Handles the "project-updated" message from the editor.
        /// The editor has already exported all JSON files to {projectPath}/build/.
        /// We queue a main-thread action to run the importer against that directory.
        /// </summary>
        private void HandleProjectUpdated(JObject payload)
        {
            string projectPath = payload?.Value<string>("projectPath") ?? "";
            if (string.IsNullOrEmpty(projectPath))
            {
                AddLogFromBackground("project-updated: missing projectPath in payload.");
                return;
            }

            string buildDirectory = Path.Combine(projectPath, "build");

            AddLogFromBackground($"Project updated. Build directory: {buildDirectory}");

            // Capture the output path value (we are on background thread, GUI fields are main-thread only)
            string targetOutputPath = outputPath;

            // Queue the import on the main thread (Unity APIs require main thread)
            QueueOnMainThread(() => PerformImport(buildDirectory, targetOutputPath));
        }

        /// <summary>
        /// Runs the StoryFlowImporter on the main thread to convert exported JSON
        /// files into Unity ScriptableObject assets.
        /// </summary>
        private void PerformImport(string buildDirectory, string targetOutputPath)
        {
            if (isImporting)
            {
                AddLog("Import already in progress, skipping.");
                return;
            }

            if (!Directory.Exists(buildDirectory))
            {
                AddLog($"Build directory not found: {buildDirectory}");
                return;
            }

            string projectJsonPath = Path.Combine(buildDirectory, "project.json");
            if (!File.Exists(projectJsonPath))
            {
                AddLog($"project.json not found in build directory: {buildDirectory}");
                return;
            }

            isImporting = true;
            Repaint();

            try
            {
                AddLog($"Importing from: {buildDirectory}");
                AddLog($"Output path: {targetOutputPath}");

                var projectAsset = StoryFlowImporter.ImportProject(buildDirectory, targetOutputPath);

                AddLog($"Import complete: {projectAsset.Title} " +
                       $"({projectAsset.ScriptReferences.Count} scripts, " +
                       $"{projectAsset.CharacterReferences.Count} characters, " +
                       $"{projectAsset.GlobalVariableEntries.Count} global variables)");

                // Ping the project asset so the user can see it in the Project window
                EditorGUIUtility.PingObject(projectAsset);
            }
            catch (Exception ex)
            {
                AddLog($"Import failed: {ex.Message}");
                Debug.LogException(ex);
            }
            finally
            {
                isImporting = false;
                Repaint();
            }
        }

        // ================================================================
        // Main Thread Queue
        // ================================================================

        /// <summary>
        /// Queues an action to be executed on the main thread during the next EditorUpdate.
        /// Thread-safe.
        /// </summary>
        private void QueueOnMainThread(Action action)
        {
            lock (queueLock)
            {
                mainThreadQueue.Enqueue(action);
            }
        }

        // ================================================================
        // Logging
        // ================================================================

        private void AddLog(string message)
        {
            string timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
            logMessages.Add(timestamped);
            if (logMessages.Count > MaxLogMessages)
                logMessages.RemoveAt(0);
        }

        /// <summary>
        /// Thread-safe log method. Queues the message and triggers a repaint.
        /// </summary>
        private void AddLogFromBackground(string message)
        {
            // EditorApplication.delayCall runs on the main thread
            string timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
            EditorApplication.delayCall += () =>
            {
                logMessages.Add(timestamped);
                if (logMessages.Count > MaxLogMessages)
                    logMessages.RemoveAt(0);
                Repaint();
            };
        }
    }
}
