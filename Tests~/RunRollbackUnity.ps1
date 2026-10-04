param(
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe',
    [string]$Workspace = (Join-Path $env:TEMP 'storyflow-rollback-unity-2022'),
    [ValidateSet('All','Tests','Build','Reload','Player')][string]$Stage = 'All'
)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$Workspace = [IO.Path]::GetFullPath($Workspace)
New-Item -ItemType Directory -Force -Path "$Workspace\Assets\Tests", "$Workspace\Assets\Editor", "$Workspace\Assets\Probe", "$Workspace\Packages", "$Workspace\ProjectSettings", "$Workspace\RollbackFixtures" | Out-Null
$dependencies = [ordered]@{
    'com.storyflow.unity' = 'file:' + $packageRoot.Replace('\','/')
    'com.unity.test-framework' = '1.1.33'
    'com.unity.ugui' = '1.0.0'
}
$builtIn = Join-Path (Split-Path $UnityPath) 'Data\Resources\PackageManager\BuiltInPackages'
Get-ChildItem -LiteralPath $builtIn -Directory | Where-Object { $_.Name -like 'com.unity.modules.*' } | ForEach-Object { $dependencies[$_.Name] = '1.0.0' }
@{ dependencies = $dependencies } | ConvertTo-Json -Depth 10 | Set-Content "$Workspace\Packages\manifest.json"
Copy-Item "$PSScriptRoot\RollbackUiTests.cs" "$Workspace\Assets\Tests\RollbackUiTests.cs"
Copy-Item "$PSScriptRoot\UnityRollbackVerification.cs" "$Workspace\Assets\Editor\UnityRollbackVerification.cs"
Copy-Item "$PSScriptRoot\RollbackPlayerProbe.cs" "$Workspace\Assets\Probe\RollbackPlayerProbe.cs"
Get-ChildItem "$PSScriptRoot\Fixtures\dialogue-rollback-v1" | Copy-Item -Destination "$Workspace\RollbackFixtures" -Recurse -Force
'{"name":"StoryFlow.RollbackEngineTests","references":["StoryFlow.Runtime","StoryFlow.Editor","Unity.TextMeshPro"],"includePlatforms":["Editor"],"optionalUnityReferences":["TestAssemblies"],"overrideReferences":true,"precompiledReferences":["Newtonsoft.Json.dll","nunit.framework.dll"]}' | Set-Content "$Workspace\Assets\Tests\StoryFlow.RollbackEngineTests.asmdef"
'{"name":"StoryFlow.RollbackProbe","references":["StoryFlow.Runtime"]}' | Set-Content "$Workspace\Assets\Probe\StoryFlow.RollbackProbe.asmdef"
'{"name":"StoryFlow.RollbackBuildVerification","references":["StoryFlow.Runtime","StoryFlow.Editor","StoryFlow.RollbackProbe"],"includePlatforms":["Editor"]}' | Set-Content "$Workspace\Assets\Editor\StoryFlow.RollbackBuildVerification.asmdef"
function Run-Unity([string]$Name, [string[]]$Extra) {
    $arguments = @('-batchmode','-nographics','-projectPath', ('"' + $Workspace + '"'), '-logFile', ('"' + "$Workspace\$Name.log" + '"')) + $Extra
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Unity $Name exited $($process.ExitCode). See $Workspace\$Name.log" }
}
if ($Stage -in @('All','Tests')) {
    Run-Unity 'test-seam-setup' @('-quit','-executeMethod','UnityRollbackVerification.ConfigureRollbackTestSeam')
    Run-Unity 'native-tests' @('-runTests','-testPlatform','EditMode','-testResults', ('"' + "$Workspace\native-results.xml" + '"'))
    [xml]$results = Get-Content "$Workspace\native-results.xml"
    if ([int]$results.'test-run'.failed -gt 0 -or [int]$results.'test-run'.total -eq 0) { throw 'Native rollback tests failed' }
    Write-Output "Unity native tests: $($results.'test-run'.passed)/$($results.'test-run'.total) passed"
}
if ($Stage -in @('All','Build')) {
    Run-Unity 'player-seam-setup' @('-quit','-executeMethod','UnityRollbackVerification.ConfigurePlayerBuild')
    Run-Unity 'player-build' @('-quit','-executeMethod','UnityRollbackVerification.BuildPlayer'); Write-Output 'Win64 player build passed'
}
if ($Stage -in @('All','Reload')) { Run-Unity 'domain-reload' @('-quit','-executeMethod','UnityRollbackVerification.VerifyReload'); Write-Output 'Editor restart/domain reload passed' }
if ($Stage -in @('All','Player')) {
    $process = Start-Process -FilePath "$Workspace\Player\RollbackProbe.exe" -ArgumentList @('-batchmode','-nographics','-logFile', ('"' + "$Workspace\player-execution.log" + '"')) -WindowStyle Hidden -PassThru
    $process.WaitForExit()
    if ($process.ExitCode -ne 0 -or !(Select-String -Path "$Workspace\player-execution.log" -Pattern 'ROLLBACK_PLAYER_EXECUTION_PASS' -Quiet)) { throw 'Player rollback execution failed' }
    Write-Output 'Win64 player execution passed'
}
Write-Output "Evidence: $Workspace"
