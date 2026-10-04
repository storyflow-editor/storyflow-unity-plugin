using System;
using UnityEngine;

public sealed class RollbackPlayerProbe : MonoBehaviour
{
    private void Start()
    {
        try
        {
            var component = GetComponent<StoryFlow.StoryFlowComponent>();
            component.StartDialogue();
            if (component.CanGoBack()) throw new Exception("first line has history");
            component.SelectOption("again");
            if (!component.CanGoBack() || component.GetIntVariable("Visits", true) != 1) throw new Exception("forward traversal");
            if (!component.GoBack().Ok || component.GetIntVariable("Visits", true) != 0) throw new Exception("restore state");
            component.SelectOption("again");
            if (component.GetIntVariable("Visits", true) != 1) throw new Exception("fresh traversal after Back");
            Debug.Log("ROLLBACK_PLAYER_EXECUTION_PASS"); Application.Quit(0);
        }
        catch (Exception error) { Debug.LogException(error); Application.Quit(1); }
    }
}
