using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TheraplyCore.Games.Contracts;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Authoring
{
    public sealed class MobileControlLayoutEditorWindow : EditorWindow
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string[] ControlTypes =
        {
            MobileControlTypes.Slider,
            MobileControlTypes.Toggle,
            MobileControlTypes.Select,
            MobileControlTypes.Number,
            MobileControlTypes.Text,
            MobileControlTypes.Button,
        };

        private static readonly string[] LayoutModes =
        {
            MobileControlLayoutModes.Stack,
            MobileControlLayoutModes.Grid,
        };

        private static readonly string[] ValueTypes =
        {
            MobileControlValueTypes.Integer,
            MobileControlValueTypes.Decimal,
            MobileControlValueTypes.Boolean,
            MobileControlValueTypes.Text,
        };

        private MobileControlLayoutAsset _asset;
        private MobileControlLayoutContract _contract;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private int _selectedControlIndex = -1;
        private string _dragControlId = string.Empty;
        private Vector2 _dragMouseStart;
        private Vector2 _dragVisualStart;
        private bool _validationExecuted;
        private bool _validationPass;
        private string _validationReason = "Validation not executed yet.";

        [MenuItem("Theraply/Scene Controller/Mobile Layout Editor")]
        public static void OpenWindow()
        {
            var window = GetWindow<MobileControlLayoutEditorWindow>("Mobile Layout Editor");
            window.minSize = new Vector2(1160f, 700f);
            window.Show();
        }

        private void OnEnable()
        {
            if (_contract == null)
            {
                _contract = MobileControlLayoutContract.CreateDefault("sample_game");
            }
        }

        private void OnGUI()
        {
            EnsureContract();
            DrawToolbar();
            DrawStatus();

            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(44f)))
            {
                _contract = MobileControlLayoutContract.CreateDefault("sample_game");
                _asset = null;
                _selectedControlIndex = -1;
                _validationExecuted = false;
                _validationReason = "Validation not executed yet.";
            }

            var nextAsset = EditorGUILayout.ObjectField(
                _asset,
                typeof(MobileControlLayoutAsset),
                false,
                GUILayout.Width(300f)) as MobileControlLayoutAsset;
            if (nextAsset != _asset)
            {
                LoadFromAsset(nextAsset);
            }

            if (GUILayout.Button("Save Asset", EditorStyles.toolbarButton, GUILayout.Width(72f)))
            {
                SaveToAsset();
            }

            if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                ValidateContract();
            }

            if (GUILayout.Button("Export Layout JSON", EditorStyles.toolbarButton, GUILayout.Width(116f)))
            {
                ExportLayoutJson();
            }

            if (GUILayout.Button("Export Schema JSON", EditorStyles.toolbarButton, GUILayout.Width(116f)))
            {
                ExportSchemaJson();
            }

            if (GUILayout.Button("Auto Layout", EditorStyles.toolbarButton, GUILayout.Width(78f)))
            {
                AutoLayoutControls();
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatus()
        {
            var status = _validationExecuted
                ? (_validationPass ? "VALID" : "INVALID")
                : "NOT VALIDATED";
            var text = _validationExecuted
                ? (_validationPass ? "Layout + schema validation passed." : _validationReason)
                : _validationReason;
            EditorGUILayout.HelpBox($"[{status}] {text}", _validationPass ? MessageType.Info : MessageType.Warning);
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(430f));
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            EditorGUILayout.LabelField("Layout Envelope", EditorStyles.boldLabel);
            _contract.schema = EditorGUILayout.TextField("Schema", _contract.schema ?? string.Empty);
            _contract.schemaVersion = EditorGUILayout.TextField("Schema Version", _contract.schemaVersion ?? string.Empty);
            _contract.gameId = EditorGUILayout.TextField("Game Id", _contract.gameId ?? string.Empty);
            _contract.title = EditorGUILayout.TextField("Title", _contract.title ?? string.Empty);
            _contract.description = EditorGUILayout.TextField("Description", _contract.description ?? string.Empty);

            _contract.layout.mode = PopupString("Layout Mode", _contract.layout.mode, LayoutModes);
            _contract.layout.columns = Mathf.Max(1, EditorGUILayout.IntField("Columns", _contract.layout.columns));

            EditorGUILayout.Space(8f);
            DrawControlsList();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawControlsList()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Controls", EditorStyles.boldLabel);
            if (GUILayout.Button("Add", GUILayout.Width(48f)))
            {
                AddControl(MobileControlTypes.Slider);
            }

            GUI.enabled = _selectedControlIndex >= 0 && _selectedControlIndex < _contract.controls.Count;
            if (GUILayout.Button("Remove", GUILayout.Width(66f)))
            {
                _contract.controls.RemoveAt(_selectedControlIndex);
                _selectedControlIndex = Mathf.Clamp(_selectedControlIndex - 1, -1, _contract.controls.Count - 1);
            }

            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Slider")) AddControl(MobileControlTypes.Slider);
            if (GUILayout.Button("Toggle")) AddControl(MobileControlTypes.Toggle);
            if (GUILayout.Button("Select")) AddControl(MobileControlTypes.Select);
            if (GUILayout.Button("Button")) AddControl(MobileControlTypes.Button);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);

            var sorted = BuildSortedControlIndices();
            for (var i = 0; i < sorted.Count; i++)
            {
                var controlIndex = sorted[i];
                var control = _contract.controls[controlIndex];
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Toggle(_selectedControlIndex == controlIndex, string.Empty, GUILayout.Width(16f)))
                {
                    _selectedControlIndex = controlIndex;
                }

                var name = string.IsNullOrWhiteSpace(control.label) ? control.controlId : control.label;
                EditorGUILayout.LabelField($"[{control.type}] {name}", GUILayout.Width(270f));
                control.order = EditorGUILayout.IntField(control.order, GUILayout.Width(56f));
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical();
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);
            DrawPreview();
            EditorGUILayout.Space(8f);
            DrawInspector();
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPreview()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Card Preview", EditorStyles.boldLabel);
            var previewRect = GUILayoutUtility.GetRect(560f, 360f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(previewRect, new Color(0.14f, 0.14f, 0.14f, 1f));

            var sorted = BuildSortedControlIndices();
            for (var i = 0; i < sorted.Count; i++)
            {
                var controlIndex = sorted[i];
                var control = _contract.controls[controlIndex];
                EnsureVisual(control);

                var rect = ComputeRect(previewRect, control, i, sorted.Count);
                var selected = _selectedControlIndex == controlIndex;
                EditorGUI.DrawRect(rect, selected ? new Color(0.16f, 0.5f, 0.95f, 0.85f) : new Color(0.3f, 0.3f, 0.3f, 0.9f));
                GUI.Label(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, 34f), BuildPreviewLabel(control), EditorStyles.whiteMiniLabel);
                HandleDrag(rect, previewRect, control, controlIndex);
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawInspector()
        {
            if (_selectedControlIndex < 0 || _selectedControlIndex >= _contract.controls.Count)
            {
                EditorGUILayout.HelpBox("Select control to edit settings and visual placement.", MessageType.Info);
                return;
            }

            var control = _contract.controls[_selectedControlIndex];
            EnsureVisual(control);
            if (control.options == null) control.options = new List<MobileControlLayoutOptionDefinition>();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Selected Control", EditorStyles.boldLabel);
            control.controlId = EditorGUILayout.TextField("Control Id", control.controlId ?? string.Empty);
            control.type = PopupString("Type", control.type, ControlTypes);
            control.label = EditorGUILayout.TextField("Label", control.label ?? string.Empty);
            control.description = EditorGUILayout.TextField("Description", control.description ?? string.Empty);
            control.sectionId = EditorGUILayout.TextField("Section Id", control.sectionId ?? string.Empty);
            control.order = EditorGUILayout.IntField("Order", control.order);
            control.defaultValue = EditorGUILayout.TextField("Default Value", control.defaultValue ?? string.Empty);

            var isButton = string.Equals(control.type, MobileControlTypes.Button, StringComparison.Ordinal);
            if (isButton)
            {
                control.buttonCommandId = EditorGUILayout.TextField("Button Command", control.buttonCommandId ?? GameCommandIds.UpdateConfig);
            }
            else
            {
                control.bindingKey = EditorGUILayout.TextField("Binding Key", control.bindingKey ?? string.Empty);
                control.valueType = PopupString("Value Type", control.valueType, ValueTypes);
                control.emitOnStartGame = EditorGUILayout.Toggle("Emit On Start", control.emitOnStartGame);
                control.emitOnUpdateConfig = EditorGUILayout.Toggle("Emit On Update", control.emitOnUpdateConfig);
            }

            control.minValue = EditorGUILayout.TextField("Min", control.minValue ?? string.Empty);
            control.maxValue = EditorGUILayout.TextField("Max", control.maxValue ?? string.Empty);
            control.step = EditorGUILayout.TextField("Step", control.step ?? string.Empty);

            EditorGUILayout.LabelField("Visual", EditorStyles.boldLabel);
            control.visual.x = EditorGUILayout.Slider("X", control.visual.x, -1f, 1f);
            control.visual.y = EditorGUILayout.Slider("Y", control.visual.y, -1f, 1f);
            control.visual.width = EditorGUILayout.Slider("Width", control.visual.width, 0.05f, 1f);
            control.visual.height = EditorGUILayout.Slider("Height", control.visual.height, 0.05f, 1f);
            control.visual.scale = EditorGUILayout.Slider("Scale", control.visual.scale, 0.2f, 4f);

            if (string.Equals(control.type, MobileControlTypes.Select, StringComparison.Ordinal))
            {
                EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
                for (var i = 0; i < control.options.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    control.options[i].value = EditorGUILayout.TextField(control.options[i].value ?? string.Empty, GUILayout.Width(150f));
                    control.options[i].label = EditorGUILayout.TextField(control.options[i].label ?? string.Empty, GUILayout.Width(150f));
                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        control.options.RemoveAt(i);
                        i--;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (GUILayout.Button("Add Option", GUILayout.Width(88f)))
                {
                    control.options.Add(new MobileControlLayoutOptionDefinition
                    {
                        value = "option_" + (control.options.Count + 1),
                        label = "Option " + (control.options.Count + 1),
                    });
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void HandleDrag(Rect controlRect, Rect previewRect, MobileControlLayoutControlDefinition control, int controlIndex)
        {
            var current = Event.current;
            if (current == null) return;

            if (current.type == EventType.MouseDown && current.button == 0 && controlRect.Contains(current.mousePosition))
            {
                _selectedControlIndex = controlIndex;
                _dragControlId = control.controlId ?? string.Empty;
                _dragMouseStart = current.mousePosition;
                _dragVisualStart = new Vector2(control.visual.x, control.visual.y);
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseDrag &&
                !string.IsNullOrWhiteSpace(_dragControlId) &&
                string.Equals(_dragControlId, control.controlId, StringComparison.Ordinal))
            {
                var delta = current.mousePosition - _dragMouseStart;
                var deltaX = previewRect.width <= 0f ? 0f : delta.x / previewRect.width;
                var deltaY = previewRect.height <= 0f ? 0f : delta.y / previewRect.height;
                var width = Mathf.Clamp(control.visual.width, 0.05f, 1f);
                var height = Mathf.Clamp(control.visual.height, 0.05f, 1f);

                control.visual.x = Mathf.Clamp(_dragVisualStart.x + deltaX, 0f, Mathf.Max(0f, 1f - width));
                control.visual.y = Mathf.Clamp(_dragVisualStart.y + deltaY, 0f, Mathf.Max(0f, 1f - height));
                current.Use();
                Repaint();
                return;
            }

            if (current.type == EventType.MouseUp && !string.IsNullOrWhiteSpace(_dragControlId))
            {
                _dragControlId = string.Empty;
                current.Use();
            }
        }

        private Rect ComputeRect(Rect previewRect, MobileControlLayoutControlDefinition control, int order, int total)
        {
            var width = Mathf.Clamp(control.visual.width, 0.05f, 1f);
            var height = Mathf.Clamp(control.visual.height, 0.05f, 1f);
            float x;
            float y;

            if (control.visual.x >= 0f && control.visual.y >= 0f)
            {
                x = Mathf.Clamp(control.visual.x, 0f, Mathf.Max(0f, 1f - width));
                y = Mathf.Clamp(control.visual.y, 0f, Mathf.Max(0f, 1f - height));
            }
            else
            {
                var columns = Mathf.Max(1, _contract.layout.columns);
                var rows = Mathf.Max(1, Mathf.CeilToInt(total / (float)columns));
                var col = order % columns;
                var row = order / columns;
                var autoWidth = Mathf.Clamp(1f / columns, 0.05f, 1f);
                var autoHeight = Mathf.Clamp(1f / rows, 0.05f, 1f);
                x = col * autoWidth;
                y = row * autoHeight;
            }

            return new Rect(
                previewRect.x + x * previewRect.width,
                previewRect.y + y * previewRect.height,
                width * previewRect.width,
                height * previewRect.height);
        }

        private void AddControl(string type)
        {
            var next = _contract.controls.Count + 1;
            var isButton = string.Equals(type, MobileControlTypes.Button, StringComparison.Ordinal);
            var isSelect = string.Equals(type, MobileControlTypes.Select, StringComparison.Ordinal);
            var control = new MobileControlLayoutControlDefinition
            {
                controlId = "control_" + next,
                type = type,
                label = "Control " + next,
                order = next - 1,
                defaultValue = isButton ? string.Empty : "0",
                bindingKey = isButton ? string.Empty : "value_" + next,
                valueType = isSelect ? MobileControlValueTypes.Text : MobileControlValueTypes.Integer,
                buttonCommandId = GameCommandIds.UpdateConfig,
                minValue = "0",
                maxValue = "1",
                step = "1",
                visual = MobileControlVisualDefinition.CreateDefault(),
                options = new List<MobileControlLayoutOptionDefinition>(),
            };

            if (isSelect)
            {
                control.options.Add(new MobileControlLayoutOptionDefinition { value = "option_1", label = "Option 1" });
                control.options.Add(new MobileControlLayoutOptionDefinition { value = "option_2", label = "Option 2" });
                control.defaultValue = "option_1";
            }

            _contract.controls.Add(control);
            _selectedControlIndex = _contract.controls.Count - 1;
            AutoLayoutControls();
        }

        private void AutoLayoutControls()
        {
            var sorted = BuildSortedControlIndices();
            var columns = Mathf.Max(1, _contract.layout.columns);
            var rows = Mathf.Max(1, Mathf.CeilToInt(sorted.Count / (float)columns));
            var width = Mathf.Clamp(1f / columns, 0.05f, 1f);
            var height = Mathf.Clamp(1f / rows, 0.05f, 1f);

            for (var i = 0; i < sorted.Count; i++)
            {
                var control = _contract.controls[sorted[i]];
                EnsureVisual(control);
                var row = i / columns;
                var col = i % columns;
                control.visual.x = col * width;
                control.visual.y = row * height;
                control.visual.width = Mathf.Clamp(width * 0.95f, 0.05f, 1f);
                control.visual.height = Mathf.Clamp(height * 0.9f, 0.05f, 1f);
            }
        }

        private List<int> BuildSortedControlIndices()
        {
            var indices = new List<int>(_contract.controls.Count);
            for (var i = 0; i < _contract.controls.Count; i++) indices.Add(i);
            indices.Sort((a, b) =>
            {
                var ca = _contract.controls[a];
                var cb = _contract.controls[b];
                var orderCompare = ca.order.CompareTo(cb.order);
                if (orderCompare != 0) return orderCompare;
                return string.Compare(ca.controlId ?? string.Empty, cb.controlId ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            });
            return indices;
        }

        private string BuildPreviewLabel(MobileControlLayoutControlDefinition control)
        {
            var label = string.IsNullOrWhiteSpace(control.label) ? control.controlId : control.label;
            return label + "\n" + control.type;
        }

        private void ValidateContract()
        {
            _validationExecuted = true;
            if (!MobileControlLayoutValidator.TryValidate(_contract, out var layoutReason))
            {
                _validationPass = false;
                _validationReason = "Layout contract invalid: " + layoutReason;
                return;
            }

            if (!MobileControlLayoutConverter.TryToMobileControlSchema(_contract, out _, out var schemaReason))
            {
                _validationPass = false;
                _validationReason = "Schema conversion invalid: " + schemaReason;
                return;
            }

            _validationPass = true;
            _validationReason = "MOBILE_LAYOUT_VALID";
        }

        private void LoadFromAsset(MobileControlLayoutAsset asset)
        {
            _asset = asset;
            _contract = asset == null
                ? MobileControlLayoutContract.CreateDefault("sample_game")
                : CloneContract(asset.contract);
            _selectedControlIndex = -1;
            _validationExecuted = false;
            _validationReason = "Validation not executed yet.";
        }

        private void SaveToAsset()
        {
            if (_asset == null)
            {
                var path = EditorUtility.SaveFilePanelInProject(
                    "Save MobileControlLayoutAsset",
                    "MobileControlLayout",
                    "asset",
                    "Choose location for MobileControlLayoutAsset.");
                if (string.IsNullOrWhiteSpace(path)) return;

                var created = CreateInstance<MobileControlLayoutAsset>();
                created.contract = CloneContract(_contract);
                AssetDatabase.CreateAsset(created, path);
                AssetDatabase.SaveAssets();
                _asset = created;
            }
            else
            {
                Undo.RecordObject(_asset, "Update MobileControlLayoutAsset");
                _asset.contract = CloneContract(_contract);
                EditorUtility.SetDirty(_asset);
                AssetDatabase.SaveAssets();
            }

            ValidateContract();
        }

        private void ExportLayoutJson()
        {
            var path = EditorUtility.SaveFilePanel(
                "Export Mobile Layout Contract JSON",
                Path.GetDirectoryName(BuildDefaultPath("mobile_control_layout")),
                Path.GetFileName(BuildDefaultPath("mobile_control_layout")),
                "json");
            if (string.IsNullOrWhiteSpace(path)) return;
            WriteJson(path, _contract);
            AssetDatabase.Refresh();
        }

        private void ExportSchemaJson()
        {
            if (!MobileControlLayoutConverter.TryToMobileControlSchema(_contract, out var schema, out var reasonCode))
            {
                EditorUtility.DisplayDialog("Schema Export Failed", "Reason: " + reasonCode, "OK");
                return;
            }

            var path = EditorUtility.SaveFilePanel(
                "Export Mobile Schema JSON",
                Path.GetDirectoryName(BuildDefaultPath("mobile_control_schema")),
                Path.GetFileName(BuildDefaultPath("mobile_control_schema")),
                "json");
            if (string.IsNullOrWhiteSpace(path)) return;
            WriteJson(path, schema);
            AssetDatabase.Refresh();
        }

        private string BuildDefaultPath(string prefix)
        {
            var projectPath = Directory.GetParent(Application.dataPath)?.FullName;
            var repoRoot = projectPath == null ? null : Directory.GetParent(projectPath)?.FullName;
            var contractsDir = string.IsNullOrWhiteSpace(repoRoot) ? Application.dataPath : Path.Combine(repoRoot, "contracts");
            Directory.CreateDirectory(contractsDir);
            var safeGameId = SanitizeFileName(_contract.gameId);
            if (string.IsNullOrWhiteSpace(safeGameId)) safeGameId = "sample_game";
            return Path.Combine(contractsDir, prefix + "_" + safeGameId + ".json");
        }

        private string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var invalidChars = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(value.Length);
            foreach (var c in value.Trim())
            {
                if (Array.IndexOf(invalidChars, c) >= 0) continue;
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-') builder.Append(char.ToLowerInvariant(c));
                else if (char.IsWhiteSpace(c)) builder.Append('_');
            }
            return builder.ToString();
        }

        private void WriteJson<T>(string path, T payload)
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            var json = JsonUtility.ToJson(payload, true);
            File.WriteAllText(path, json + Environment.NewLine, Utf8NoBom);
            Debug.Log("[MobileLayoutEditor] Exported: " + path);
        }

        private string PopupString(string label, string currentValue, string[] options)
        {
            var selected = 0;
            for (var i = 0; i < options.Length; i++)
            {
                if (string.Equals(options[i], currentValue, StringComparison.OrdinalIgnoreCase))
                {
                    selected = i;
                    break;
                }
            }

            var next = EditorGUILayout.Popup(label, selected, options);
            return options[next];
        }

        private MobileControlLayoutContract CloneContract(MobileControlLayoutContract source)
        {
            var json = JsonUtility.ToJson(source ?? MobileControlLayoutContract.CreateDefault("sample_game"));
            return JsonUtility.FromJson<MobileControlLayoutContract>(json);
        }

        private void EnsureContract()
        {
            if (_contract == null) _contract = MobileControlLayoutContract.CreateDefault("sample_game");
            if (_contract.layout == null) _contract.layout = MobileControlLayoutCanvasDefinition.CreateDefault();
            if (_contract.payload == null) _contract.payload = MobileControlLayoutContractPayloadDefinition.CreateDefault();
            if (_contract.sections == null) _contract.sections = new List<MobileControlLayoutGroupDefinition>();
            if (_contract.controls == null) _contract.controls = new List<MobileControlLayoutControlDefinition>();
        }

        private void EnsureVisual(MobileControlLayoutControlDefinition control)
        {
            if (control.visual == null) control.visual = MobileControlVisualDefinition.CreateDefault();
        }
    }
}
