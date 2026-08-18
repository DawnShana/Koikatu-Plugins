from __future__ import print_function
from pathlib import Path

ROOT = Path(__file__).resolve().parent
SRC = (ROOT / 'KK_DragCoordinateLoadBridge.cs').read_text(encoding='utf-8')
BUILD = (ROOT / 'build.bat').read_text(encoding='utf-8', errors='replace')

checks = []
def check(name, cond):
    checks.append((name, bool(cond)))

check('Plugin version is 1.2.1', 'PluginVersion = "1.2.1"' in SRC)
check('Loads in CharaStudio', '[BepInProcess("CharaStudio")]' in SRC)
check('Loads in Koikatu', '[BepInProcess("Koikatu")]' in SRC)
check('Only one HarmonyInstance.Patch call', SRC.count('HarmonyInstance.Patch(') == 1)
check('Studio target remains StudioHandler', 'DragAndDrop.StudioHandler' in SRC)
check('Maker target remains MakerHandler', 'DragAndDrop.MakerHandler' in SRC)
check('Maker prefix is fail-closed after accepted path', 'Maker preparation failed; full DragAndDrop load suppressed' in SRC)
check('Maker busy protection retained', 'MakerAdapter.IsUnderlyingLoadBusy()' in SRC)
check('Maker short deferred retry retained', 'DeferredPrepareMaker' in SRC and 'const int MaxRetryFrames = 12;' in SRC)
check('Last-drop generation retained', 'generation != DropGeneration' in SRC)
check('Maker real list is not injected', 'MakerSelectProxy' in SRC and 'MakerListCtrlProxy' in SRC)
check('Maker native Load button maintained in LateUpdate', 'MakerAdapter.MaintainPreparedLoadButton();' in SRC)
check('CLO panel IsActive semantic retained', '_panelIsActiveMethod.Invoke(panel, null)' in SRC)
check('CLO coordinatePath readback retained', 'CLO Maker branch did not accept the dropped coordinate path.' in SRC)
check('CLO live Maker window binding checked', 'initializedCloWindow' in SRC)
check('Native Maker UI navigation occurs before CLO panel readiness', SRC.index('PrepareResult navigationResult = OpenCoordinateLoadWindowThroughMakerUi(fileWindow);', SRC.index('internal PrepareResult PrepareDroppedCoordinate(string path)')) < SRC.index('object panel = _panelField.GetValue(null);', SRC.index('internal PrepareResult PrepareDroppedCoordinate(string path)')))
check('Main System uses audited semantic index 6', 'const int MainSystemIndex = 6;' in SRC)
check('No Transform ancestry guessing', 'FindOwningToggleIndex' not in SRC and 'IsChildOf(parentTransform)' not in SRC)
check('CoordinateLoad enum matched by name', 'Enum.Parse(_fileWindowTypeProperty.PropertyType, "CoordinateLoad", false)' in SRC)
check('No numeric CoordinateLoad == 3 runtime hardpin', 'numericType != 3' not in SRC and 'currentWindowTypeNumeric != 3' not in SRC and 'audited value 3' not in SRC)
check('No Maker Harmony patch-table gate', 'HasRequiredCloMakerPatches' not in SRC)
check('No Maker-only dead Harmony helpers after adapter start', 'ContainsPatchMethod' not in SRC[SRC.index('internal sealed class MakerCoordinateLoadOptionAdapter'):])
check('No second custom UI', 'new GameObject' not in SRC[SRC.index('internal sealed class MakerCoordinateLoadOptionAdapter'):])
check('Build version is 1.2.1', 'v1.2.1' in BUILD)
check('Build uses Framework csc', 'csc.exe' in BUILD)
check('Build uses /nostdlib+', '/nostdlib+' in BUILD)
check('Build uses /langversion:4', '/langversion:4' in BUILD)
import re
check('Build does not invoke dotnet', re.search(r'(?im)^\s*dotnet(?:\.exe)?\b', BUILD) is None)
check('Build does not invoke nuget', re.search(r'(?im)^\s*nuget(?:\.exe)?\b', BUILD) is None)

# Lightweight lexical balance that ignores comments/strings/chars.
pairs = {'{': '}', '(': ')', '[': ']'}
stack = []
state = 'code'
i = 0
ok = True
while i < len(SRC):
    c = SRC[i]
    n = SRC[i + 1] if i + 1 < len(SRC) else ''
    if state == 'code':
        if c == '/' and n == '/': state = 'line'; i += 2; continue
        if c == '/' and n == '*': state = 'block'; i += 2; continue
        if c == '"': state = 'string'; i += 1; continue
        if c == "'": state = 'char'; i += 1; continue
        if c in pairs: stack.append(c)
        elif c in '})]':
            if not stack or pairs[stack[-1]] != c:
                ok = False; break
            stack.pop()
    elif state == 'line':
        if c == '\n': state = 'code'
    elif state == 'block':
        if c == '*' and n == '/': state = 'code'; i += 2; continue
    elif state == 'string':
        if c == '\\': i += 2; continue
        if c == '"': state = 'code'
    elif state == 'char':
        if c == '\\': i += 2; continue
        if c == "'": state = 'code'
    i += 1
check('Lexical state closes cleanly', ok and state in ('code', 'line'))
check('Brace/parenthesis/bracket balance', ok and not stack)

for name, passed in checks:
    print(('PASS  ' if passed else 'FAIL  ') + name)
print('\n%d/%d PASS' % (sum(1 for _, p in checks if p), len(checks)))
print('Compiler/runtime execution: NOT RUN in this environment.')
if not all(p for _, p in checks):
    raise SystemExit(1)
