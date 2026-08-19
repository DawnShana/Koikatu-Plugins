from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent
CS = (ROOT / "KK_DragCoordinateLoadBridge.cs").read_text(encoding="utf-8-sig")
BAT = (ROOT / "build.bat").read_text(encoding="utf-8-sig", errors="replace")

checks = []
def check(name, cond):
    checks.append((name, bool(cond)))

check("Plugin version is 1.2.3", 'PluginVersion = "1.2.3"' in CS)
check("Loads in CharaStudio", '[BepInProcess("CharaStudio")]' in CS)
check("Loads in Koikatu", '[BepInProcess("Koikatu")]' in CS)
check("Only one HarmonyInstance.Patch call", CS.count("HarmonyInstance.Patch(") == 1)
check("Studio target remains StudioHandler", '"DragAndDrop.StudioHandler"' in CS)
check("Maker target remains MakerHandler", '"DragAndDrop.MakerHandler"' in CS)
check("Maker valid drop remains fail-closed", "MakerCoordinateLoadPrefix" in CS and "return false;" in CS)
check("Maker intercept log states original DnD is suppressed", "original DragAndDrop whole-coordinate load will be suppressed" in CS)
check("Maker busy guard retained", "IsUnderlyingLoadBusy()" in CS)
check("Prepared Maker path is not consumed merely because CLO becomes busy", "SetButtonInteractable(_preparedLoadButton, false);" in CS)
check("Maker short deferred retry retained", "DeferredPrepare" in CS and "12" in CS)
check("Last-drop generation retained", "DropGeneration" in CS)
check("Maker real list is not injected", "AddList(" not in CS and ".Add(" not in CS[CS.find("internal sealed class MakerCoordinateLoadOptionAdapter"):])
check("Maker native Load button maintained in LateUpdate", "LateUpdate" in CS and "MaintainPreparedLoadButton" in CS)
check("CLO panel IsActive semantic retained", "_panelIsActiveMethod" in CS)
check("CLO coordinatePath readback retained", "_coordinatePathField.GetValue(null) as string" in CS)
check("No pre-OnSelect CLO CustomFileWindow circular gate", "CLO Maker window binding is not initialized yet" not in CS)
check("Post-OnSelect CLO window verification retained", "CLO Maker branch did not bind the active CustomFileWindow" in CS)
check("Maker has dedicated CLO Show Selection opener", "EnsureCloSelectionPanelOpen" in CS)
check("CLO real Show Selection hierarchy names used", "CoordinateLoadPanel" in CS and "CoordinateLoadBtn" in CS)
check("CLO Show Selection invokes Button onClick", "Button.onClick.Invoke()" in CS and "invokeMethod.Invoke(onClick, null)" in CS)
check("Show Selection does not click again when already active", "activeRaw is bool && (bool)activeRaw" in CS)
check("Show Selection verifies CLO panel after click", "Show Selection button ran, but its selection panel is not active yet" in CS)
check("Show Selection waits for active native Coordinate Load hierarchy", "showSelectionButton.gameObject.activeInHierarchy" in CS)
maker = CS[CS.find("internal sealed class MakerCoordinateLoadOptionAdapter"):]
check("Maker no longer directly opens CLO panel with SetActive(true)", "panelComponent.gameObject.SetActive(true)" not in maker)
check("Maker does not partially rollback only panel.activeSelf", "oldPanelActive" not in maker)
check("Path drift releases ownership rather than fighting later user selection", "coordinatePath changed away from the pending dragged card" in CS)
check("Path drift disables the bridge-armed Load button before release", "SetButtonInteractable(previousButton, false)" in CS)
maker_prepare = maker[maker.find("internal PrepareResult PrepareDroppedCoordinate"): ]
check("Native Maker UI navigation occurs before CLO panel readiness", 0 <= maker_prepare.find("OpenCoordinateLoadWindowThroughMakerUi(fileWindow)") < maker_prepare.find("object panel = _panelField.GetValue(null)"))
check("Main System uses audited semantic index 6", "const int MainSystemIndex = 6;" in CS)
check("No Transform ancestry guessing for Maker System tab", "IsChildOf" not in maker and "IsChildOf" not in maker)
check("No redundant Maker CanvasGroup readiness gate", "CanvasGroup" not in maker)
check("CoordinateLoad enum matched by name", 'Enum.Parse(_fileWindowTypeProperty.PropertyType, "CoordinateLoad", false)' in CS)
check("No numeric CoordinateLoad == 3 runtime hardpin", "== 3" not in maker and "!= 3" not in maker)
check("No Maker Harmony patch-table gate", "Harmony.GetPatchInfo" not in maker)
check("No second custom UI created by Maker bridge", "new GameObject(" not in maker and "CreateButton" not in maker)
check("Maker bridge does not patch CLO methods", "HarmonyInstance.Patch(" not in maker)
check("Maker bridge does not load/change ChaControl directly", "LoadFile(" not in maker and "ChangeClothes(" not in maker and "ChangeAccessory(" not in maker)
check("Build version is 1.2.3", "v1.2.3" in BAT)
check("Build temp tag is v123", "v123" in BAT)
check("Build uses Framework csc", "csc.exe" in BAT.lower())
check("Build uses /nostdlib+", "/nostdlib+" in BAT)
check("Build uses /langversion:4", "/langversion:4" in BAT)

def active_bat_lines(text):
    out=[]
    for line in text.splitlines():
        t=line.strip()
        low=t.lower()
        if not t or low.startswith("rem ") or low.startswith("::") or low.startswith("echo "):
            continue
        out.append(t)
    return "\n".join(out)
ACTIVE_BAT = active_bat_lines(BAT)
check("Build does not invoke dotnet", not re.search(r"(^|[&|\s])dotnet(?:\.exe)?([\s]|$)", ACTIVE_BAT, re.I | re.M))
check("Build does not invoke nuget", not re.search(r"(^|[&|\s])nuget(?:\.exe)?([\s]|$)", ACTIVE_BAT, re.I | re.M))

# Lightweight C# lexical scanner for delimiter balance.
def scan_balances(text):
    stack=[]
    i=0
    n=len(text)
    state='code'
    while i<n:
        c=text[i]
        nxt=text[i+1] if i+1<n else ''
        if state=='code':
            if c=='/' and nxt=='/': state='line'; i+=2; continue
            if c=='/' and nxt=='*': state='block'; i+=2; continue
            if c=='@' and nxt=='"': state='verbatim'; i+=2; continue
            if c=='"': state='string'; i+=1; continue
            if c=="'": state='char'; i+=1; continue
            if c in '({[': stack.append(c)
            elif c in ')}]':
                want={')':'(', '}':'{', ']':'['}[c]
                if not stack or stack[-1]!=want: return False, False
                stack.pop()
            i+=1; continue
        if state=='line':
            if c=='\n': state='code'
            i+=1; continue
        if state=='block':
            if c=='*' and nxt=='/': state='code'; i+=2; continue
            i+=1; continue
        if state=='string':
            if c=='\\': i+=2; continue
            if c=='"': state='code'
            i+=1; continue
        if state=='char':
            if c=='\\': i+=2; continue
            if c=="'": state='code'
            i+=1; continue
        if state=='verbatim':
            if c=='"' and nxt=='"': i+=2; continue
            if c=='"': state='code'
            i+=1; continue
    return state=='code', not stack

lex_ok, balance_ok = scan_balances(CS)
check("Lexical state closes cleanly", lex_ok)
check("Brace/parenthesis/bracket balance", balance_ok)

passed = sum(1 for _, ok in checks if ok)
for name, ok in checks:
    print(("PASS  " if ok else "FAIL  ") + name)
print("\n%d/%d PASS" % (passed, len(checks)))
print("Compiler/runtime execution: NOT RUN in this environment.")
sys.exit(0 if passed == len(checks) else 1)
