using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheraplyCore.Games.Contracts;
using UnityEditor;
using UnityEngine;

namespace TheraplyCore.Editor.Authoring
{
    public sealed class SessionFlowGraphEditorWindow : EditorWindow
    {
        private static readonly string[] NodeTypes =
        {
            TaskGraphNodeTypes.Action,
            TaskGraphNodeTypes.Condition,
            TaskGraphNodeTypes.Branch,
            TaskGraphNodeTypes.Timer,
            TaskGraphNodeTypes.Complete,
            TaskGraphNodeTypes.Fail,
        };

        private static readonly string[] ControlModes =
        {
            SessionFlowControlModes.Hybrid,
            SessionFlowControlModes.RemoteOnly,
            SessionFlowControlModes.LocalOnly,
        };

        private static readonly string[] ChannelIds =
        {
            SessionFlowChannelIds.Pointer,
            SessionFlowChannelIds.ToolImpact,
            SessionFlowChannelIds.HandContact,
            SessionFlowChannelIds.HandGrab,
            SessionFlowChannelIds.Gaze,
            SessionFlowChannelIds.Breath,
            SessionFlowChannelIds.AudioSource,
            SessionFlowChannelIds.DualHand,
            SessionFlowChannelIds.PosePath,
            SessionFlowChannelIds.Sequence,
            SessionFlowChannelIds.Timeline,
        };

        private const float LeftWidth = 390f;
        private const float RightWidth = 430f;
        private const float CanvasWidth = 3600f;
        private const float CanvasHeight = 2400f;
        private const float NodeWidth = 230f;
        private const float NodeHeight = 150f;
        private const float MinGraphZoom = 0.45f;
        private const float MaxGraphZoom = 2.2f;
        private const float GraphZoomStep = 0.08f;

        private GameDefinitionAsset _asset;
        private GameDefinition _definition;
        private readonly Dictionary<string, Vector2> _nodePositions =
            new Dictionary<string, Vector2>(StringComparer.OrdinalIgnoreCase);

        private Vector2 _leftScroll;
        private Vector2 _graphScroll;
        private Vector2 _rightScroll;
        private float _graphZoom = 1f;
        private bool _middlePanActive;
        private string _renameNodeId = string.Empty;
        private string _renameDraft = string.Empty;

        private string _selectedNodeId = string.Empty;
        private bool _layoutDirty = true;
        private bool _hasValidation;
        private bool _validationPass;
        private string _validationReason = string.Empty;

        [MenuItem("Theraply/Session Flow/Flow Graph Editor")]
        public static void OpenWindow()
        {
            var window = GetWindow<SessionFlowGraphEditorWindow>();
            window.titleContent = new GUIContent("Flow Graph Editor");
            window.minSize = new Vector2(1450f, 780f);
            window.Show();
        }

        private void OnEnable()
        {
            EnsureDefinition();
            AutoLayout();
        }

        private void OnGUI()
        {
            EnsureDefinition();

            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawGraphPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var nextAsset = EditorGUILayout.ObjectField(
                _asset,
                typeof(GameDefinitionAsset),
                false,
                GUILayout.Width(260f)) as GameDefinitionAsset;
            if (nextAsset != _asset)
            {
                _asset = nextAsset;
                if (_asset != null)
                {
                    LoadFromAsset(_asset);
                }
            }

            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(52f)))
            {
                _definition = GameDefinition.CreateSample();
                EnsureDefinition();
                _layoutDirty = true;
            }

            if (GUILayout.Button("Load Asset", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                LoadFromAsset(_asset);
            }

            if (GUILayout.Button("Save Asset", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                SaveToAsset();
            }

            if (GUILayout.Button("Import JSON", EditorStyles.toolbarButton, GUILayout.Width(92f)))
            {
                ImportJson();
            }

            if (GUILayout.Button("Export JSON", EditorStyles.toolbarButton, GUILayout.Width(92f)))
            {
                ExportJson();
            }

            if (GUILayout.Button("Export Contracts", EditorStyles.toolbarButton, GUILayout.Width(116f)))
            {
                SaveToAsset();
                if (_asset != null)
                {
                    SessionFlowAuthoringExport.ExportAllToContractsMenu();
                }
            }

            if (GUILayout.Button("Auto Layout", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                AutoLayout();
            }

            if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                _validationPass = SessionFlowDefinitionValidator.TryValidate(_definition, out _validationReason);
                _hasValidation = true;
            }

            GUILayout.Space(10f);
            GUILayout.Label("Zoom " + Mathf.RoundToInt(_graphZoom * 100f) + "%", EditorStyles.miniLabel, GUILayout.Width(78f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(LeftWidth));
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            EditorGUILayout.LabelField("Game", EditorStyles.boldLabel);
            _definition.gameId = EditorGUILayout.TextField("Game Id", _definition.gameId);
            _definition.displayName = EditorGUILayout.TextField("Display Name", _definition.displayName);
            _definition.commentVersion = EditorGUILayout.TextField("Comment Version", _definition.commentVersion);
            _definition.controlMode = DrawPopup("Control Mode", _definition.controlMode, ControlModes);

            EnsureConfig();
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Config", EditorStyles.boldLabel);
            _definition.config.difficulty = EditorGUILayout.TextField("Difficulty", _definition.config.difficulty);
            _definition.config.timeLimitSec = EditorGUILayout.IntField("Time Limit", _definition.config.timeLimitSec);
            _definition.config.targetCount = EditorGUILayout.IntField("Target Count", _definition.config.targetCount);
            DrawKeyValues("Config Extras", _definition.config.extras);

            EditorGUILayout.Space(8f);
            DrawChannels();

            EditorGUILayout.Space(8f);
            DrawPolicies();

            EditorGUILayout.Space(8f);
            DrawGraphSettings();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawGraphPanel()
        {
            EditorGUILayout.BeginVertical();
            var host = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.ExpandHeight(true),
                GUILayout.ExpandWidth(true));
            GUI.Box(host, GUIContent.none);

            HandleGraphCanvasInput(host);

            var canvasSize = GetZoomedCanvasSize();
            var canvas = new Rect(0f, 0f, canvasSize.x, canvasSize.y);
            _graphScroll = ClampGraphScroll(_graphScroll, host, canvasSize);
            _graphScroll = GUI.BeginScrollView(host, _graphScroll, canvas);

            if (_layoutDirty)
            {
                AutoLayout();
                _layoutDirty = false;
            }

            DrawLinks();

            BeginWindows();
            for (var i = 0; i < _definition.taskGraph.nodes.Count; i++)
            {
                var node = _definition.taskGraph.nodes[i];
                if (node == null)
                {
                    continue;
                }

                var id = SafeNodeId(node.nodeId, i);
                var pos = ResolveNodePosition(id, i);
                var rect = new Rect(
                    pos.x * _graphZoom,
                    pos.y * _graphZoom,
                    NodeWidth * _graphZoom,
                    NodeHeight * _graphZoom);
                var selected = string.Equals(node.nodeId, _selectedNodeId, StringComparison.OrdinalIgnoreCase);
                var isEntryNode = string.Equals(
                    node.nodeId,
                    _definition.taskGraph.entryNodeId,
                    StringComparison.OrdinalIgnoreCase);
                var titlePrefix = selected ? "* " : string.Empty;
                if (isEntryNode)
                {
                    titlePrefix += "[ENTRY] ";
                }
                var title = titlePrefix + node.nodeId + " (" + node.nodeType + ")";

                var updated = GUI.Window(
                    1000 + i,
                    rect,
                    windowId => DrawNodeCard(windowId, node),
                    title);

                if (updated.position != rect.position)
                {
                    _nodePositions[id] = updated.position / Mathf.Max(0.001f, _graphZoom);
                }
            }
            EndWindows();

            GUI.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void HandleGraphCanvasInput(Rect host)
        {
            var current = Event.current;
            if (current == null)
            {
                return;
            }

            if (_middlePanActive &&
                current.rawType == EventType.MouseUp &&
                current.button == 2)
            {
                _middlePanActive = false;
                Repaint();
            }

            if (!host.Contains(current.mousePosition))
            {
                return;
            }

            if (current.type == EventType.ScrollWheel)
            {
                var previousZoom = _graphZoom;
                var nextZoom = Mathf.Clamp(
                    previousZoom * (1f - current.delta.y * GraphZoomStep),
                    MinGraphZoom,
                    MaxGraphZoom);
                if (!Mathf.Approximately(previousZoom, nextZoom))
                {
                    var localPointer = current.mousePosition - host.position;
                    var stableCanvasPoint =
                        (_graphScroll + localPointer) / Mathf.Max(0.001f, previousZoom);
                    _graphZoom = nextZoom;
                    _graphScroll = stableCanvasPoint * _graphZoom - localPointer;
                    _graphScroll = ClampGraphScroll(_graphScroll, host, GetZoomedCanvasSize());
                    Repaint();
                }

                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 2)
            {
                _middlePanActive = true;
                current.Use();
                return;
            }

            if (_middlePanActive &&
                current.type == EventType.MouseDrag &&
                (current.button == 2 || current.button == 0))
            {
                _graphScroll -= current.delta;
                _graphScroll = ClampGraphScroll(_graphScroll, host, GetZoomedCanvasSize());
                Repaint();
                current.Use();
                return;
            }

            if (_middlePanActive &&
                current.type == EventType.MouseUp &&
                (current.button == 2 || current.button == 0))
            {
                _middlePanActive = false;
                current.Use();
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 1)
            {
                var canvasPosition = ToCanvasPosition(host, current.mousePosition);
                if (IsPointerOverNode(canvasPosition))
                {
                    return;
                }

                ShowGraphContextMenu(canvasPosition);
                current.Use();
            }
        }

        private void ShowGraphContextMenu(Vector2 canvasPosition)
        {
            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("Add Node/Actions/Action"),
                false,
                () => AddNode(TaskGraphNodeTypes.Action, canvasPosition, autoLayoutAfterAdd: false));
            menu.AddItem(
                new GUIContent("Add Node/Logic/Condition"),
                false,
                () => AddNode(TaskGraphNodeTypes.Condition, canvasPosition, autoLayoutAfterAdd: false));
            menu.AddItem(
                new GUIContent("Add Node/Logic/Branch"),
                false,
                () => AddNode(TaskGraphNodeTypes.Branch, canvasPosition, autoLayoutAfterAdd: false));
            menu.AddItem(
                new GUIContent("Add Node/Flow/Timer"),
                false,
                () => AddNode(TaskGraphNodeTypes.Timer, canvasPosition, autoLayoutAfterAdd: false));
            menu.AddItem(
                new GUIContent("Add Node/Terminal/Complete"),
                false,
                () => AddNode(TaskGraphNodeTypes.Complete, canvasPosition, autoLayoutAfterAdd: false));
            menu.AddItem(
                new GUIContent("Add Node/Terminal/Fail"),
                false,
                () => AddNode(TaskGraphNodeTypes.Fail, canvasPosition, autoLayoutAfterAdd: false));
            menu.AddSeparator("Tools/");
            menu.AddItem(new GUIContent("Tools/Auto Layout"), false, AutoLayout);
            menu.AddItem(
                new GUIContent("Tools/Validate"),
                false,
                () =>
                {
                    _validationPass =
                        SessionFlowDefinitionValidator.TryValidate(_definition, out _validationReason);
                    _hasValidation = true;
                });
            menu.AddSeparator("View/");
            menu.AddItem(
                new GUIContent("View/Reset Zoom"),
                false,
                () =>
                {
                    _graphZoom = 1f;
                    _graphScroll = Vector2.zero;
                    Repaint();
                });
            menu.ShowAsContext();
        }

        private bool IsPointerOverNode(Vector2 canvasPosition)
        {
            for (var i = 0; i < _definition.taskGraph.nodes.Count; i++)
            {
                var node = _definition.taskGraph.nodes[i];
                if (node == null)
                {
                    continue;
                }

                if (NodeRect(node.nodeId, i).Contains(canvasPosition))
                {
                    return true;
                }
            }

            return false;
        }

        private Vector2 ToCanvasPosition(Rect host, Vector2 pointer)
        {
            var local = pointer - host.position;
            return (_graphScroll + local) / Mathf.Max(0.001f, _graphZoom);
        }

        private Vector2 GetZoomedCanvasSize()
        {
            return new Vector2(CanvasWidth * _graphZoom, CanvasHeight * _graphZoom);
        }

        private static Vector2 ClampGraphScroll(Vector2 scroll, Rect host, Vector2 canvasSize)
        {
            var maxX = Mathf.Max(0f, canvasSize.x - host.width);
            var maxY = Mathf.Max(0f, canvasSize.y - host.height);
            return new Vector2(
                Mathf.Clamp(scroll.x, 0f, maxX),
                Mathf.Clamp(scroll.y, 0f, maxY));
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(RightWidth));
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            if (!_hasValidation)
            {
                EditorGUILayout.HelpBox("Validation not executed yet.", MessageType.Info);
            }
            else if (_validationPass)
            {
                EditorGUILayout.HelpBox("SessionFlowDefinitionValidator: PASS", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "SessionFlowDefinitionValidator: FAIL\nreasonCode=" +
                    (string.IsNullOrWhiteSpace(_validationReason)
                        ? SessionFlowDefinitionReasonCodes.DefinitionParseFailed
                        : _validationReason),
                    MessageType.Error);
            }

            EditorGUILayout.Space(8f);
            DrawNodeInspector();

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawNodeCard(int windowId, TaskGraphNodeDefinition node)
        {
            HandleNodeContextInput(node);

            EditorGUILayout.LabelField("Success", string.IsNullOrWhiteSpace(node.nextOnSuccess) ? "-" : node.nextOnSuccess);
            EditorGUILayout.LabelField("Fail", string.IsNullOrWhiteSpace(node.nextOnFail) ? "-" : node.nextOnFail);
            EditorGUILayout.LabelField("Timeout", string.IsNullOrWhiteSpace(node.nextOnTimeout) ? "-" : node.nextOnTimeout);
            EditorGUILayout.LabelField("Allowed", (node.allowedActions == null ? 0 : node.allowedActions.Count).ToString());
            EditorGUILayout.LabelField("Conditions", (node.conditions == null ? 0 : node.conditions.Count).ToString());
            if (GUILayout.Button("Select"))
            {
                _selectedNodeId = node.nodeId;
            }

            GUI.DragWindow(new Rect(0f, 0f, NodeWidth * _graphZoom, 24f * _graphZoom));
        }

        private void HandleNodeContextInput(TaskGraphNodeDefinition node)
        {
            var current = Event.current;
            if (current == null || node == null)
            {
                return;
            }

            if (current.type != EventType.MouseDown || current.button != 1)
            {
                return;
            }

            _selectedNodeId = node.nodeId;
            ShowNodeContextMenu(node.nodeId);
            current.Use();
        }

        private void ShowNodeContextMenu(string nodeId)
        {
            var node = GetNode(nodeId);
            if (node == null)
            {
                return;
            }

            var hasOutgoing =
                !string.IsNullOrWhiteSpace(node.nextOnSuccess) ||
                !string.IsNullOrWhiteSpace(node.nextOnFail) ||
                !string.IsNullOrWhiteSpace(node.nextOnTimeout) ||
                HasConditionTargets(node);
            var hasIncoming = HasIncomingLinks(nodeId);

            var menu = new GenericMenu();
            menu.AddItem(
                new GUIContent("Node/Select"),
                true,
                () => _selectedNodeId = nodeId);
            menu.AddItem(
                new GUIContent("Node/Rename"),
                false,
                () => BeginRenameNode(nodeId));
            menu.AddItem(
                new GUIContent("Node/Set As Entry (Start)"),
                false,
                () => SetEntryNode(nodeId));
            menu.AddSeparator("Node/");
            menu.AddItem(
                new GUIContent("Node/Delete"),
                false,
                () => RemoveNode(nodeId));

            menu.AddSeparator("Connections/");
            if (hasOutgoing)
            {
                menu.AddItem(
                    new GUIContent("Connections/Disconnect Outgoing"),
                    false,
                    () => DisconnectNodeOutgoing(nodeId));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Connections/Disconnect Outgoing"));
            }

            if (hasIncoming)
            {
                menu.AddItem(
                    new GUIContent("Connections/Disconnect Incoming"),
                    false,
                    () => DisconnectNodeIncoming(nodeId));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Connections/Disconnect Incoming"));
            }

            if (hasOutgoing || hasIncoming)
            {
                menu.AddItem(
                    new GUIContent("Connections/Disconnect All"),
                    false,
                    () =>
                    {
                        DisconnectNodeOutgoing(nodeId);
                        DisconnectNodeIncoming(nodeId);
                    });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Connections/Disconnect All"));
            }

            menu.AddSeparator("Effects/");
            menu.AddItem(
                new GUIContent("Effects/Add On Enter/Spawn Prefab Wave"),
                false,
                () => AddSpawnWavePresetToNode(nodeId));
            menu.AddItem(
                new GUIContent("Effects/Add On Enter/Random Material Color"),
                false,
                () => AddRandomMaterialColorPresetToNode(nodeId));

            menu.ShowAsContext();
        }

        private void DrawNodeInspector()
        {
            EditorGUILayout.LabelField("Node Inspector", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Action"))
            {
                AddNode(TaskGraphNodeTypes.Action);
            }
            if (GUILayout.Button("Add Condition"))
            {
                AddNode(TaskGraphNodeTypes.Condition);
            }
            if (GUILayout.Button("Add Branch"))
            {
                AddNode(TaskGraphNodeTypes.Branch);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Timer"))
            {
                AddNode(TaskGraphNodeTypes.Timer);
            }
            if (GUILayout.Button("Add Complete"))
            {
                AddNode(TaskGraphNodeTypes.Complete);
            }
            if (GUILayout.Button("Add Fail"))
            {
                AddNode(TaskGraphNodeTypes.Fail);
            }
            EditorGUILayout.EndHorizontal();

            var nodeIds = BuildNodeIds();
            if (nodeIds.Count == 0)
            {
                EditorGUILayout.HelpBox("No nodes in graph.", MessageType.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_selectedNodeId) || !nodeIds.Contains(_selectedNodeId))
            {
                _selectedNodeId = nodeIds[0];
            }

            var selectedIndex = Mathf.Max(0, nodeIds.IndexOf(_selectedNodeId));
            selectedIndex = EditorGUILayout.Popup("Selected Node", selectedIndex, nodeIds.ToArray());
            _selectedNodeId = nodeIds[selectedIndex];

            var node = GetNode(_selectedNodeId);
            if (node == null)
            {
                return;
            }

            DrawRenameSection(node);

            var editedNodeId = EditorGUILayout.DelayedTextField("Node Id", node.nodeId);
            if (!string.Equals(editedNodeId, node.nodeId, StringComparison.Ordinal))
            {
                RenameNode(node.nodeId, editedNodeId);
                node = GetNode(_selectedNodeId);
                if (node == null)
                {
                    return;
                }
            }
            node.nodeType = DrawPopup("Node Type", node.nodeType, NodeTypes);
            node.timeoutSec = EditorGUILayout.FloatField("Timeout Sec", node.timeoutSec);

            node.nextOnSuccess = DrawNodeTarget("Next On Success", node.nextOnSuccess);
            node.nextOnFail = DrawNodeTarget("Next On Fail", node.nextOnFail);
            node.nextOnTimeout = DrawNodeTarget("Next On Timeout", node.nextOnTimeout);

            EditorGUILayout.Space(6f);
            DrawAllowedActions(node);
            EditorGUILayout.Space(6f);
            DrawConditions(node);
            EditorGUILayout.Space(6f);
            DrawEffects("On Enter", node.onEnterEffects);
            DrawEffects("On Exit", node.onExitEffects);
            DrawEffects("On Accepted", node.onAcceptedEffects);
            DrawEffects("On Rejected", node.onRejectedEffects);
            DrawEffects("On Timeout", node.onTimeoutEffects);

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Remove Selected Node"))
            {
                RemoveNode(node.nodeId);
            }
        }

        private void DrawRenameSection(TaskGraphNodeDefinition node)
        {
            var renamingThisNode = string.Equals(node.nodeId, _renameNodeId, StringComparison.OrdinalIgnoreCase);
            if (!renamingThisNode)
            {
                return;
            }

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Rename Node", EditorStyles.miniBoldLabel);
            _renameDraft = EditorGUILayout.TextField("New Id", _renameDraft);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Rename"))
            {
                var applied = RenameNode(node.nodeId, _renameDraft);
                if (applied)
                {
                    _renameNodeId = string.Empty;
                    _renameDraft = string.Empty;
                }
            }

            if (GUILayout.Button("Cancel Rename"))
            {
                _renameNodeId = string.Empty;
                _renameDraft = string.Empty;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void DrawAllowedActions(TaskGraphNodeDefinition node)
        {
            node.allowedActions ??= new List<AllowedActionDefinition>();
            EditorGUILayout.LabelField("Allowed Actions", EditorStyles.boldLabel);
            for (var i = 0; i < node.allowedActions.Count; i++)
            {
                var item = node.allowedActions[i] ?? new AllowedActionDefinition();
                node.allowedActions[i] = item;
                item.constraints ??= new List<KeyValuePairString>();

                EditorGUILayout.BeginVertical("box");
                item.actionId = EditorGUILayout.TextField("Action Id", item.actionId);
                item.targetGroup = EditorGUILayout.TextField("Target Group", item.targetGroup);
                DrawKeyValues("Constraints", item.constraints);
                if (GUILayout.Button("Remove Allowed Action"))
                {
                    node.allowedActions.RemoveAt(i);
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add Allowed Action"))
            {
                node.allowedActions.Add(new AllowedActionDefinition());
            }
        }

        private void DrawConditions(TaskGraphNodeDefinition node)
        {
            node.conditions ??= new List<ConditionDefinition>();
            EditorGUILayout.LabelField("Conditions", EditorStyles.boldLabel);
            for (var i = 0; i < node.conditions.Count; i++)
            {
                var item = node.conditions[i] ?? new ConditionDefinition();
                node.conditions[i] = item;

                EditorGUILayout.BeginVertical("box");
                item.conditionId = EditorGUILayout.TextField("Condition Id", item.conditionId);
                item.subject = EditorGUILayout.TextField("Subject", item.subject);
                item.op = EditorGUILayout.TextField("Operator", item.op);
                item.value = EditorGUILayout.TextField("Value", item.value);
                item.nextNodeId = DrawNodeTarget("Next Node", item.nextNodeId);
                if (GUILayout.Button("Remove Condition"))
                {
                    node.conditions.RemoveAt(i);
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add Condition"))
            {
                node.conditions.Add(new ConditionDefinition());
            }
        }

        private void DrawEffects(string title, List<EffectDefinition> effects)
        {
            effects ??= new List<EffectDefinition>();
            EditorGUILayout.LabelField(title + " Effects", EditorStyles.boldLabel);
            for (var i = 0; i < effects.Count; i++)
            {
                var item = effects[i] ?? new EffectDefinition();
                effects[i] = item;
                item.parameters ??= new List<KeyValuePairString>();

                EditorGUILayout.BeginVertical("box");
                item.effectId = EditorGUILayout.TextField("Effect Id", item.effectId);
                item.binding = EditorGUILayout.TextField("Binding", item.binding);
                DrawKeyValues("Parameters", item.parameters);
                if (GUILayout.Button("Remove Effect"))
                {
                    effects.RemoveAt(i);
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Add Effect"))
            {
                effects.Add(new EffectDefinition());
            }

            if (GUILayout.Button("Add Spawn Prefab Wave"))
            {
                effects.Add(CreateSpawnWavePresetEffect());
            }

            if (GUILayout.Button("Add Random Material Color"))
            {
                effects.Add(CreateRandomMaterialColorPresetEffect());
            }
        }
        private void DrawChannels()
        {
            _definition.channels ??= SessionFlowDefaults.CreateDefaultChannels();
            EditorGUILayout.LabelField("Channels", EditorStyles.boldLabel);

            for (var i = 0; i < ChannelIds.Length; i++)
            {
                var channelId = ChannelIds[i];
                var channel = _definition.channels.FirstOrDefault(
                    item => item != null && string.Equals(item.channelId, channelId, StringComparison.OrdinalIgnoreCase));
                if (channel == null)
                {
                    channel = new SessionFlowChannelConfig { channelId = channelId, enabled = false };
                    _definition.channels.Add(channel);
                }

                channel.enabled = EditorGUILayout.ToggleLeft(channel.channelId, channel.enabled);
            }
        }

        private void DrawPolicies()
        {
            _definition.policies ??= SessionFlowPolicies.CreateDefault();
            _definition.policies.retryPolicy ??= new RetryPolicy();
            _definition.policies.timeoutPolicy ??= new TimeoutPolicy();
            _definition.policies.branchPolicy ??= new BranchPolicy();
            _definition.policies.scoringPolicy ??= new ScoringPolicy();
            _definition.policies.difficultyPolicy ??= new DifficultyPolicy();
            _definition.policies.safetyPolicy ??= new SafetyPolicy();
            _definition.policies.controlPolicy ??= new ControlPolicy();
            _definition.policies.telemetryPolicy ??= new TelemetryPolicy();
            _definition.policies.localizationPolicy ??= new LocalizationPolicy();
            _definition.policies.calendarPolicy ??= new CalendarPolicy();
            _definition.policies.localizationPolicy.fallbackLocales ??= new List<string>();

            EditorGUILayout.LabelField("Policies", EditorStyles.boldLabel);

            _definition.policies.wrongActionPolicy = EditorGUILayout.TextField("Wrong Action", _definition.policies.wrongActionPolicy);
            _definition.policies.retryPolicy.maxRetries = EditorGUILayout.IntField("Retry Max", _definition.policies.retryPolicy.maxRetries);
            _definition.policies.retryPolicy.cooldownSec = EditorGUILayout.FloatField("Retry Cooldown", _definition.policies.retryPolicy.cooldownSec);
            _definition.policies.timeoutPolicy.defaultTimeoutSec = EditorGUILayout.FloatField("Default Timeout", _definition.policies.timeoutPolicy.defaultTimeoutSec);
            _definition.policies.branchPolicy.precedence = EditorGUILayout.TextField("Branch Precedence", _definition.policies.branchPolicy.precedence);
            _definition.policies.controlPolicy.mode = DrawPopup("Policy Control Mode", _definition.policies.controlPolicy.mode, ControlModes);

            _definition.policies.scoringPolicy.lives = EditorGUILayout.IntField("Lives", _definition.policies.scoringPolicy.lives);
            _definition.policies.scoringPolicy.pointsPerCorrect = EditorGUILayout.IntField("Points Correct", _definition.policies.scoringPolicy.pointsPerCorrect);
            _definition.policies.scoringPolicy.pointsPerWrong = EditorGUILayout.IntField("Points Wrong", _definition.policies.scoringPolicy.pointsPerWrong);

            _definition.policies.difficultyPolicy.adaptiveEnabled = EditorGUILayout.Toggle("Adaptive Difficulty", _definition.policies.difficultyPolicy.adaptiveEnabled);
            _definition.policies.safetyPolicy.emergencyStopEnabled = EditorGUILayout.Toggle("Emergency Stop", _definition.policies.safetyPolicy.emergencyStopEnabled);
            _definition.policies.safetyPolicy.rejectDisabledChannels = EditorGUILayout.Toggle("Reject Disabled", _definition.policies.safetyPolicy.rejectDisabledChannels);
            _definition.policies.telemetryPolicy.requireDecisionForEveryAction = EditorGUILayout.Toggle("Require Decision", _definition.policies.telemetryPolicy.requireDecisionForEveryAction);

            _definition.policies.localizationPolicy.defaultLocale = EditorGUILayout.TextField("Default Locale", _definition.policies.localizationPolicy.defaultLocale);
            _definition.policies.localizationPolicy.fallbackToLanguageCode = EditorGUILayout.Toggle("Fallback Language", _definition.policies.localizationPolicy.fallbackToLanguageCode);
            DrawStringList("Fallback Locales", _definition.policies.localizationPolicy.fallbackLocales);

            _definition.policies.calendarPolicy.timezoneId = EditorGUILayout.TextField("Calendar Timezone", _definition.policies.calendarPolicy.timezoneId);
            _definition.policies.calendarPolicy.fallbackToUtcWhenTimezoneInvalid = EditorGUILayout.Toggle("Calendar Fallback UTC", _definition.policies.calendarPolicy.fallbackToUtcWhenTimezoneInvalid);
            _definition.policies.calendarPolicy.allowQaTimeOverride = EditorGUILayout.Toggle("Calendar QA Override", _definition.policies.calendarPolicy.allowQaTimeOverride);
        }

        private void DrawGraphSettings()
        {
            EnsureGraph();
            EditorGUILayout.LabelField("Task Graph", EditorStyles.boldLabel);

            var ids = BuildNodeIds();
            if (ids.Count > 0)
            {
                var idx = Mathf.Max(0, ids.IndexOf(_definition.taskGraph.entryNodeId));
                idx = EditorGUILayout.Popup("Entry Node", idx, ids.ToArray());
                _definition.taskGraph.entryNodeId = ids[idx];
            }
            else
            {
                _definition.taskGraph.entryNodeId = EditorGUILayout.TextField("Entry Node", _definition.taskGraph.entryNodeId);
            }

            EditorGUILayout.LabelField("Node Count", _definition.taskGraph.nodes.Count.ToString());
        }

        private void DrawKeyValues(string title, List<KeyValuePairString> values)
        {
            values ??= new List<KeyValuePairString>();
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            for (var i = 0; i < values.Count; i++)
            {
                var item = values[i] ?? new KeyValuePairString();
                values[i] = item;
                EditorGUILayout.BeginHorizontal();
                item.key = EditorGUILayout.TextField(item.key);
                item.value = EditorGUILayout.TextField(item.value);
                if (GUILayout.Button("-", GUILayout.Width(22f)))
                {
                    values.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add", GUILayout.Width(64f)))
            {
                values.Add(new KeyValuePairString());
            }
        }

        private void DrawStringList(string title, List<string> values)
        {
            values ??= new List<string>();
            EditorGUILayout.LabelField(title, EditorStyles.miniBoldLabel);
            for (var i = 0; i < values.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                values[i] = EditorGUILayout.TextField(values[i]);
                if (GUILayout.Button("-", GUILayout.Width(22f)))
                {
                    values.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }

            if (GUILayout.Button("+ Add", GUILayout.Width(64f)))
            {
                values.Add(string.Empty);
            }
        }

        private void AddNode(
            string nodeType,
            Vector2? canvasPosition = null,
            bool autoLayoutAfterAdd = true)
        {
            EnsureGraph();
            var node = new TaskGraphNodeDefinition
            {
                nodeId = BuildNodeId(nodeType),
                nodeType = nodeType,
                allowedActions = new List<AllowedActionDefinition>(),
                conditions = new List<ConditionDefinition>(),
                onEnterEffects = new List<EffectDefinition>(),
                onExitEffects = new List<EffectDefinition>(),
                onAcceptedEffects = new List<EffectDefinition>(),
                onRejectedEffects = new List<EffectDefinition>(),
                onTimeoutEffects = new List<EffectDefinition>(),
            };

            _definition.taskGraph.nodes.Add(node);
            _selectedNodeId = node.nodeId;
            if (canvasPosition.HasValue)
            {
                var requested = canvasPosition.Value;
                var clamped = new Vector2(
                    Mathf.Clamp(requested.x - NodeWidth * 0.5f, 8f, CanvasWidth - NodeWidth - 8f),
                    Mathf.Clamp(requested.y - NodeHeight * 0.5f, 8f, CanvasHeight - NodeHeight - 8f));
                _nodePositions[SafeNodeId(node.nodeId, _definition.taskGraph.nodes.Count - 1)] = clamped;
            }

            _layoutDirty = autoLayoutAfterAdd;
            Repaint();
        }

        private void RemoveNode(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            var idx = IndexOfNode(nodeId);
            if (idx < 0)
            {
                return;
            }

            var removedId = _definition.taskGraph.nodes[idx].nodeId;
            DisconnectNodeIncoming(removedId);
            _definition.taskGraph.nodes.RemoveAt(idx);
            _nodePositions.Remove(SafeNodeId(removedId, idx));
            if (string.Equals(_definition.taskGraph.entryNodeId, removedId, StringComparison.OrdinalIgnoreCase))
            {
                _definition.taskGraph.entryNodeId = _definition.taskGraph.nodes.Count > 0
                    ? _definition.taskGraph.nodes[0].nodeId
                    : string.Empty;
            }

            if (_definition.taskGraph.nodes.Count > 0)
            {
                _selectedNodeId = _definition.taskGraph.nodes[0].nodeId;
            }
            else
            {
                _selectedNodeId = string.Empty;
            }

            if (string.Equals(_renameNodeId, removedId, StringComparison.OrdinalIgnoreCase))
            {
                _renameNodeId = string.Empty;
                _renameDraft = string.Empty;
            }

            _layoutDirty = true;
        }

        private bool RenameNode(string sourceNodeId, string requestedNodeId)
        {
            if (string.IsNullOrWhiteSpace(sourceNodeId))
            {
                return false;
            }

            var sourceIndex = IndexOfNode(sourceNodeId);
            if (sourceIndex < 0)
            {
                return false;
            }

            var normalized = NormalizeNodeId(requestedNodeId);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                ShowNotification(new GUIContent("Node Id cannot be empty."));
                return false;
            }

            var targetNodeId = BuildUniqueNodeId(normalized, sourceNodeId);
            var node = _definition.taskGraph.nodes[sourceIndex];
            var originalNodeId = node.nodeId;
            if (string.Equals(originalNodeId, targetNodeId, StringComparison.Ordinal))
            {
                return true;
            }

            node.nodeId = targetNodeId;

            if (string.Equals(_definition.taskGraph.entryNodeId, originalNodeId, StringComparison.OrdinalIgnoreCase))
            {
                _definition.taskGraph.entryNodeId = targetNodeId;
            }

            if (string.Equals(targetNodeId, "start", StringComparison.OrdinalIgnoreCase))
            {
                _definition.taskGraph.entryNodeId = targetNodeId;
            }

            for (var i = 0; i < _definition.taskGraph.nodes.Count; i++)
            {
                var other = _definition.taskGraph.nodes[i];
                if (other == null)
                {
                    continue;
                }

                if (string.Equals(other.nextOnSuccess, originalNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    other.nextOnSuccess = targetNodeId;
                }
                if (string.Equals(other.nextOnFail, originalNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    other.nextOnFail = targetNodeId;
                }
                if (string.Equals(other.nextOnTimeout, originalNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    other.nextOnTimeout = targetNodeId;
                }

                if (other.conditions == null)
                {
                    continue;
                }

                for (var c = 0; c < other.conditions.Count; c++)
                {
                    var condition = other.conditions[c];
                    if (condition != null &&
                        string.Equals(condition.nextNodeId, originalNodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        condition.nextNodeId = targetNodeId;
                    }
                }
            }

            var oldPositionKey = SafeNodeId(originalNodeId, sourceIndex);
            var newPositionKey = SafeNodeId(targetNodeId, sourceIndex);
            if (_nodePositions.TryGetValue(oldPositionKey, out var savedPosition))
            {
                _nodePositions.Remove(oldPositionKey);
                _nodePositions[newPositionKey] = savedPosition;
            }

            if (string.Equals(_selectedNodeId, originalNodeId, StringComparison.OrdinalIgnoreCase))
            {
                _selectedNodeId = targetNodeId;
            }

            if (string.Equals(_renameNodeId, originalNodeId, StringComparison.OrdinalIgnoreCase))
            {
                _renameNodeId = targetNodeId;
                _renameDraft = targetNodeId;
            }

            if (!string.Equals(normalized, targetNodeId, StringComparison.Ordinal))
            {
                ShowNotification(new GUIContent("Node Id already existed. Renamed to " + targetNodeId + "."));
            }

            Repaint();
            return true;
        }

        private void SetEntryNode(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            if (IndexOfNode(nodeId) < 0)
            {
                return;
            }

            _definition.taskGraph.entryNodeId = nodeId.Trim();
            _selectedNodeId = nodeId.Trim();
            Repaint();
        }

        private void BeginRenameNode(string nodeId)
        {
            var node = GetNode(nodeId);
            if (node == null)
            {
                return;
            }

            _selectedNodeId = node.nodeId;
            _renameNodeId = node.nodeId;
            _renameDraft = node.nodeId;
            Repaint();
        }

        private static string NormalizeNodeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value.Trim().Replace(' ', '_');
        }

        private string BuildUniqueNodeId(string desiredNodeId, string currentNodeId)
        {
            var normalized = NormalizeNodeId(desiredNodeId);
            if (string.Equals(normalized, currentNodeId, StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            if (IndexOfNode(normalized) < 0)
            {
                return normalized;
            }

            var suffix = 1;
            while (true)
            {
                var candidate = normalized + "_" + suffix;
                var candidateIndex = IndexOfNode(candidate);
                if (candidateIndex < 0 ||
                    string.Equals(candidate, currentNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                suffix++;
            }
        }

        private bool HasConditionTargets(TaskGraphNodeDefinition node)
        {
            if (node?.conditions == null)
            {
                return false;
            }

            for (var i = 0; i < node.conditions.Count; i++)
            {
                var condition = node.conditions[i];
                if (condition != null && !string.IsNullOrWhiteSpace(condition.nextNodeId))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasIncomingLinks(string targetNodeId)
        {
            if (string.IsNullOrWhiteSpace(targetNodeId))
            {
                return false;
            }

            for (var i = 0; i < _definition.taskGraph.nodes.Count; i++)
            {
                var node = _definition.taskGraph.nodes[i];
                if (node == null)
                {
                    continue;
                }

                if (string.Equals(node.nextOnSuccess, targetNodeId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.nextOnFail, targetNodeId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(node.nextOnTimeout, targetNodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (node.conditions == null)
                {
                    continue;
                }

                for (var c = 0; c < node.conditions.Count; c++)
                {
                    var condition = node.conditions[c];
                    if (condition != null &&
                        string.Equals(condition.nextNodeId, targetNodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void DisconnectNodeOutgoing(string nodeId)
        {
            var node = GetNode(nodeId);
            if (node == null)
            {
                return;
            }

            node.nextOnSuccess = string.Empty;
            node.nextOnFail = string.Empty;
            node.nextOnTimeout = string.Empty;
            if (node.conditions != null)
            {
                for (var i = 0; i < node.conditions.Count; i++)
                {
                    var condition = node.conditions[i];
                    if (condition != null)
                    {
                        condition.nextNodeId = string.Empty;
                    }
                }
            }

            Repaint();
        }

        private void DisconnectNodeIncoming(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            for (var i = 0; i < _definition.taskGraph.nodes.Count; i++)
            {
                var node = _definition.taskGraph.nodes[i];
                if (node == null)
                {
                    continue;
                }

                if (string.Equals(node.nextOnSuccess, nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    node.nextOnSuccess = string.Empty;
                }
                if (string.Equals(node.nextOnFail, nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    node.nextOnFail = string.Empty;
                }
                if (string.Equals(node.nextOnTimeout, nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    node.nextOnTimeout = string.Empty;
                }

                if (node.conditions == null)
                {
                    continue;
                }

                for (var c = 0; c < node.conditions.Count; c++)
                {
                    var condition = node.conditions[c];
                    if (condition != null &&
                        string.Equals(condition.nextNodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                    {
                        condition.nextNodeId = string.Empty;
                    }
                }
            }

            Repaint();
        }

        private void AddSpawnWavePresetToNode(string nodeId)
        {
            var node = GetNode(nodeId);
            if (node == null)
            {
                return;
            }

            node.onEnterEffects ??= new List<EffectDefinition>();
            node.onEnterEffects.Add(CreateSpawnWavePresetEffect());
            _selectedNodeId = node.nodeId;
            Repaint();
        }

        private void AddRandomMaterialColorPresetToNode(string nodeId)
        {
            var node = GetNode(nodeId);
            if (node == null)
            {
                return;
            }

            node.onEnterEffects ??= new List<EffectDefinition>();
            node.onEnterEffects.Add(CreateRandomMaterialColorPresetEffect());
            _selectedNodeId = node.nodeId;
            Repaint();
        }

        private static EffectDefinition CreateSpawnWavePresetEffect()
        {
            return new EffectDefinition
            {
                effectId = "spawn_prefab_wave",
                binding = string.Empty,
                parameters = new List<KeyValuePairString>
                {
                    new KeyValuePairString { key = "prefabKey", value = "target_prefab" },
                    new KeyValuePairString { key = "bindingKeyPrefix", value = "targets" },
                    new KeyValuePairString { key = "spawnPointKey", value = "spawn_center" },
                    new KeyValuePairString { key = "count", value = "5" },
                    new KeyValuePairString { key = "durationSec", value = "2.0" },
                    new KeyValuePairString { key = "areaSizeX", value = "2.0" },
                    new KeyValuePairString { key = "areaSizeY", value = "0.0" },
                    new KeyValuePairString { key = "areaSizeZ", value = "2.0" },
                    new KeyValuePairString { key = "randomYaw", value = "true" },
                    new KeyValuePairString { key = "randomColorOnSpawn", value = "false" },
                    new KeyValuePairString { key = "colorIncludeInactive", value = "true" },
                    new KeyValuePairString { key = "minHue", value = "0.0" },
                    new KeyValuePairString { key = "maxHue", value = "1.0" },
                    new KeyValuePairString { key = "minSaturation", value = "0.55" },
                    new KeyValuePairString { key = "maxSaturation", value = "0.95" },
                    new KeyValuePairString { key = "minValue", value = "0.60" },
                    new KeyValuePairString { key = "maxValue", value = "1.0" },
                    new KeyValuePairString { key = "alpha", value = "1.0" },
                },
            };
        }

        private static EffectDefinition CreateRandomMaterialColorPresetEffect()
        {
            return new EffectDefinition
            {
                effectId = "set_random_material_color",
                binding = string.Empty,
                parameters = new List<KeyValuePairString>
                {
                    new KeyValuePairString { key = "spawnBindingPrefix", value = "targets" },
                    new KeyValuePairString { key = "includeInactive", value = "true" },
                    new KeyValuePairString { key = "minHue", value = "0.0" },
                    new KeyValuePairString { key = "maxHue", value = "1.0" },
                    new KeyValuePairString { key = "minSaturation", value = "0.55" },
                    new KeyValuePairString { key = "maxSaturation", value = "0.95" },
                    new KeyValuePairString { key = "minValue", value = "0.60" },
                    new KeyValuePairString { key = "maxValue", value = "1.0" },
                    new KeyValuePairString { key = "alpha", value = "1.0" },
                },
            };
        }
        private void DrawLinks()
        {
            Handles.BeginGUI();
            try
            {
                for (var i = 0; i < _definition.taskGraph.nodes.Count; i++)
                {
                    var source = _definition.taskGraph.nodes[i];
                    if (source == null)
                    {
                        continue;
                    }

                    var sourceRect = NodeRectScaled(source.nodeId, i);
                    var links = BuildLinks(source);
                    for (var l = 0; l < links.Count; l++)
                    {
                        var link = links[l];
                        var targetIndex = IndexOfNode(link.Target);
                        if (targetIndex < 0)
                        {
                            continue;
                        }

                        var target = _definition.taskGraph.nodes[targetIndex];
                        var targetRect = NodeRectScaled(target.nodeId, targetIndex);

                        var start = new Vector3(sourceRect.xMax, sourceRect.center.y, 0f);
                        var end = new Vector3(targetRect.xMin, targetRect.center.y, 0f);
                        var tan = Mathf.Clamp(Mathf.Abs(end.x - start.x) * 0.4f, 40f, 180f);

                        Handles.color = link.Color;
                        Handles.DrawBezier(
                            start,
                            end,
                            start + Vector3.right * tan,
                            end + Vector3.left * tan,
                            link.Color,
                            null,
                            2f);

                        var labelPos = (start + end) * 0.5f;
                        GUI.Label(new Rect(labelPos.x - 55f, labelPos.y - 10f, 110f, 18f), link.Label, EditorStyles.miniBoldLabel);
                    }
                }
            }
            finally
            {
                Handles.EndGUI();
            }
        }

        private List<Link> BuildLinks(TaskGraphNodeDefinition node)
        {
            var links = new List<Link>();
            if (!string.IsNullOrWhiteSpace(node.nextOnSuccess))
            {
                links.Add(new Link(node.nextOnSuccess, "success", new Color(0.2f, 0.8f, 0.2f, 0.95f)));
            }
            if (!string.IsNullOrWhiteSpace(node.nextOnFail))
            {
                links.Add(new Link(node.nextOnFail, "fail", new Color(0.95f, 0.35f, 0.35f, 0.95f)));
            }
            if (!string.IsNullOrWhiteSpace(node.nextOnTimeout))
            {
                links.Add(new Link(node.nextOnTimeout, "timeout", new Color(0.98f, 0.72f, 0.25f, 0.95f)));
            }

            if (node.conditions != null)
            {
                for (var i = 0; i < node.conditions.Count; i++)
                {
                    var c = node.conditions[i];
                    if (c == null || string.IsNullOrWhiteSpace(c.nextNodeId))
                    {
                        continue;
                    }

                    var label = string.IsNullOrWhiteSpace(c.conditionId) ? "condition" : c.conditionId;
                    links.Add(new Link(c.nextNodeId, label, new Color(0.4f, 0.6f, 1f, 0.95f)));
                }
            }

            return links;
        }

        private Rect NodeRect(string nodeId, int fallback)
        {
            var id = SafeNodeId(nodeId, fallback);
            var pos = ResolveNodePosition(id, fallback);
            return new Rect(pos.x, pos.y, NodeWidth, NodeHeight);
        }

        private Rect NodeRectScaled(string nodeId, int fallback)
        {
            var unscaled = NodeRect(nodeId, fallback);
            return new Rect(
                unscaled.x * _graphZoom,
                unscaled.y * _graphZoom,
                unscaled.width * _graphZoom,
                unscaled.height * _graphZoom);
        }

        private Vector2 ResolveNodePosition(string nodeId, int fallback)
        {
            if (!_nodePositions.TryGetValue(nodeId, out var pos))
            {
                pos = new Vector2(80f + (fallback % 5) * 300f, 80f + (fallback / 5) * 220f);
                _nodePositions[nodeId] = pos;
            }

            return pos;
        }

        private string SafeNodeId(string nodeId, int fallback)
        {
            return string.IsNullOrWhiteSpace(nodeId) ? "node_" + Mathf.Max(0, fallback) : nodeId.Trim();
        }

        private void AutoLayout()
        {
            EnsureGraph();
            _nodePositions.Clear();
            for (var i = 0; i < _definition.taskGraph.nodes.Count; i++)
            {
                var node = _definition.taskGraph.nodes[i];
                if (node == null)
                {
                    continue;
                }

                var col = i % 4;
                var row = i / 4;
                _nodePositions[SafeNodeId(node.nodeId, i)] = new Vector2(80f + col * 320f, 90f + row * 230f);
            }
        }

        private void EnsureDefinition()
        {
            if (_definition == null)
            {
                _definition = GameDefinition.CreateSample();
            }

            EnsureConfig();
            EnsureGraph();
        }

        private void EnsureConfig()
        {
            _definition.config ??= new GameDefinitionConfig();
            _definition.config.extras ??= new List<KeyValuePairString>();
        }

        private void EnsureGraph()
        {
            _definition.taskGraph ??= TaskGraphDefinition.CreateEmpty();
            _definition.taskGraph.nodes ??= new List<TaskGraphNodeDefinition>();

            for (var i = 0; i < _definition.taskGraph.nodes.Count; i++)
            {
                var node = _definition.taskGraph.nodes[i];
                if (node == null)
                {
                    node = new TaskGraphNodeDefinition
                    {
                        nodeId = "node_" + (i + 1),
                        nodeType = TaskGraphNodeTypes.Action,
                    };
                    _definition.taskGraph.nodes[i] = node;
                }

                node.allowedActions ??= new List<AllowedActionDefinition>();
                node.conditions ??= new List<ConditionDefinition>();
                node.onEnterEffects ??= new List<EffectDefinition>();
                node.onExitEffects ??= new List<EffectDefinition>();
                node.onAcceptedEffects ??= new List<EffectDefinition>();
                node.onRejectedEffects ??= new List<EffectDefinition>();
                node.onTimeoutEffects ??= new List<EffectDefinition>();
            }

            if (string.IsNullOrWhiteSpace(_definition.taskGraph.entryNodeId) && _definition.taskGraph.nodes.Count > 0)
            {
                _definition.taskGraph.entryNodeId = _definition.taskGraph.nodes[0].nodeId;
            }
        }

        private void LoadFromAsset(GameDefinitionAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            _definition = Clone(asset.definition) ?? GameDefinition.CreateSample();
            EnsureDefinition();
            _layoutDirty = true;
        }

        private void SaveToAsset()
        {
            if (_asset == null)
            {
                var path = EditorUtility.SaveFilePanelInProject(
                    "Save GameDefinitionAsset",
                    "GameDefinition",
                    "asset",
                    "Choose location for the GameDefinitionAsset.");
                if (string.IsNullOrWhiteSpace(path))
                {
                    return;
                }

                var created = CreateInstance<GameDefinitionAsset>();
                created.definition = Clone(_definition) ?? GameDefinition.CreateSample();
                AssetDatabase.CreateAsset(created, path);
                AssetDatabase.SaveAssets();
                _asset = created;
                Selection.activeObject = created;
                return;
            }

            Undo.RecordObject(_asset, "Save Flow Asset");
            _asset.definition = Clone(_definition) ?? GameDefinition.CreateSample();
            EditorUtility.SetDirty(_asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private void ImportJson()
        {
            var path = EditorUtility.OpenFilePanel("Import GameDefinition JSON", Application.dataPath, "json");
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var parsed = JsonUtility.FromJson<GameDefinition>(json);
                if (parsed == null)
                {
                    EditorUtility.DisplayDialog("Flow Graph Editor", "Could not parse JSON.", "OK");
                    return;
                }

                _definition = parsed;
                EnsureDefinition();
                _layoutDirty = true;
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Flow Graph Editor", "Import failed:\n" + ex.Message, "OK");
            }
        }

        private void ExportJson()
        {
            var path = EditorUtility.SaveFilePanel(
                "Export GameDefinition JSON",
                Application.dataPath,
                (_definition.gameId ?? "game_definition") + ".json",
                "json");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                File.WriteAllText(path, JsonUtility.ToJson(_definition, true));
                AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Flow Graph Editor", "Export failed:\n" + ex.Message, "OK");
            }
        }

        private TaskGraphNodeDefinition GetNode(string nodeId)
        {
            return _definition.taskGraph.nodes.FirstOrDefault(
                node => node != null && string.Equals(node.nodeId, nodeId, StringComparison.OrdinalIgnoreCase));
        }

        private int IndexOfNode(string nodeId)
        {
            for (var i = 0; i < _definition.taskGraph.nodes.Count; i++)
            {
                var node = _definition.taskGraph.nodes[i];
                if (node != null && string.Equals(node.nodeId, nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private List<string> BuildNodeIds()
        {
            return _definition.taskGraph.nodes
                .Where(node => node != null && !string.IsNullOrWhiteSpace(node.nodeId))
                .Select(node => node.nodeId.Trim())
                .ToList();
        }

        private string BuildNodeId(string prefix)
        {
            var safePrefix = string.IsNullOrWhiteSpace(prefix) ? "node" : prefix.Trim().ToLowerInvariant();
            safePrefix = safePrefix.Replace(' ', '_');

            var idx = 1;
            while (true)
            {
                var candidate = safePrefix + "_" + idx;
                if (IndexOfNode(candidate) < 0)
                {
                    return candidate;
                }
                idx++;
            }
        }

        private string DrawNodeTarget(string label, string current)
        {
            var ids = BuildNodeIds();
            if (ids.Count == 0)
            {
                return EditorGUILayout.TextField(label, current);
            }

            var options = new List<string> { string.Empty };
            options.AddRange(ids);
            var idx = Mathf.Max(0, options.IndexOf(current ?? string.Empty));
            idx = EditorGUILayout.Popup(label, idx, options.ToArray());
            return options[idx];
        }

        private string DrawPopup(string label, string current, IReadOnlyList<string> options)
        {
            var value = string.IsNullOrWhiteSpace(current) ? options[0] : current.Trim();
            var idx = 0;
            for (var i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i], value, StringComparison.OrdinalIgnoreCase))
                {
                    idx = i;
                    break;
                }
            }

            idx = EditorGUILayout.Popup(label, idx, options.ToArray());
            return options[Mathf.Clamp(idx, 0, options.Count - 1)];
        }

        private static GameDefinition Clone(GameDefinition source)
        {
            if (source == null)
            {
                return null;
            }

            var json = JsonUtility.ToJson(source);
            return string.IsNullOrWhiteSpace(json) ? null : JsonUtility.FromJson<GameDefinition>(json);
        }

        private readonly struct Link
        {
            public readonly string Target;
            public readonly string Label;
            public readonly Color Color;

            public Link(string target, string label, Color color)
            {
                Target = target;
                Label = label;
                Color = color;
            }
        }
    }
}
