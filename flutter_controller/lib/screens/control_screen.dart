import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_controller/models/content_delivery_contract.dart';
import 'package:flutter_controller/models/critical_command_envelope.dart';
import 'package:flutter_controller/models/device_info.dart';
import 'package:flutter_controller/models/entitlement_access.dart';
import 'package:flutter_controller/models/guided_session_continuation_policy.dart';
import 'package:flutter_controller/models/guided_session_plan.dart';
import 'package:flutter_controller/models/mobile_control_schema.dart';
import 'package:flutter_controller/models/ops_error_catalog.dart';
import 'package:flutter_controller/models/parent_progress_snapshot.dart';
import 'package:flutter_controller/models/runtime_status_signal.dart';
import 'package:flutter_controller/models/session_attach_failure_policy.dart';
import 'package:flutter_controller/models/session_fsm_contract.dart';
import 'package:flutter_controller/models/session_recovery_manager.dart';
import 'package:flutter_controller/models/session_ownership.dart';
import 'package:flutter_controller/models/session_recovery_policy.dart';
import 'package:flutter_controller/models/student.dart';
import 'package:flutter_controller/models/student_reward_unlock.dart';
import 'package:flutter_controller/models/therapist_session_settings.dart';
import 'package:flutter_controller/services/connection_service.dart';
import 'package:flutter_controller/services/discovery_service.dart';
import 'package:flutter_controller/services/entitlement_service.dart';
import 'package:flutter_controller/services/firebase_service.dart';
import 'package:flutter_controller/services/foreground_service_bridge.dart';
import 'package:flutter_controller/services/operator_incident_popup_queue.dart';
import 'package:flutter_controller/services/parent_progress_service.dart';
import 'package:flutter_controller/services/game_data_service.dart';
import 'package:flutter_controller/services/session_journal_service.dart';
import 'package:flutter_controller/services/student_reward_service.dart';
import 'package:flutter_controller/services/therapist_session_settings_service.dart';
import 'package:flutter_controller/models/game_run_record.dart';
import 'package:flutter_controller/models/therapy_session_record.dart';
import 'package:flutter_controller/widgets/media_stream_widget.dart';
import 'package:flutter_controller/widgets/mobile_control_renderer.dart';

enum _SessionGateAction { resume, startNew }

enum _WorkflowStep { gameCatalog, gameSetup }

enum _ExitChoice { keepUnfinished, endSession, cancel }

class ControlScreen extends StatefulWidget {
  final DeviceInfo device;
  final Student student;
  final DiscoveryService discoveryService;

  const ControlScreen({
    super.key,
    required this.device,
    required this.student,
    required this.discoveryService,
  });

  @override
  State<ControlScreen> createState() => _ControlScreenState();
}

class _ControlScreenState extends State<ControlScreen>
    with WidgetsBindingObserver, SingleTickerProviderStateMixin {
  static final bool _contentDeliveryEnabled = true;
  static const int _sessionIngestPort = 18765;
  static const String _setVideoBitrateCommandId = 'WEBRTC_SET_VIDEO_BITRATE';
  static final bool _serverAuthoritativeHandoffGate = true;
  static const bool _showCatalogRescueTerminateButton = false;

  static MobileControlSchemaParseResult _parseLegacyBundledSchema({
    required String expectedGameId,
    required String gameConfigType,
    required Map<String, dynamic> staticFields,
    required List<Map<String, dynamic>> controls,
    String title = '',
    String description = '',
    List<Map<String, dynamic>> sections = const <Map<String, dynamic>>[
      <String, dynamic>{
        'sectionId': 'setup',
        'label': '',
        'order': 10,
      },
    ],
  }) {
    return MobileControlSchema.tryParse(
      <String, dynamic>{
        'schema': 'THERAPLY_MOBILE_CONTROL_SCHEMA',
        'schemaVersion': '2026-03-16',
        'gameId': expectedGameId,
        'title': title,
        'description': description,
        'layout': const <String, dynamic>{
          'mode': 'stack',
          'columns': 1,
        },
        'payload': <String, dynamic>{
          'target': 'game_config',
          'gameConfigType': gameConfigType,
          'gameConfigVersion': 1,
          'includeVersionInGameConfig': false,
          'staticFields': staticFields,
        },
        'sections': sections,
        'controls': controls,
      },
      expectedGameId: expectedGameId,
    );
  }

  static Map<String, dynamic> _schemaOption(Object value, String label) {
    return <String, dynamic>{
      'value': value.toString(),
      'label': label,
    };
  }

  static Map<String, dynamic> _schemaSelectControl({
    required String controlId,
    required String label,
    required String path,
    required String defaultValue,
    required int order,
    required List<Map<String, dynamic>> options,
    String hint = '',
    String sectionId = 'setup',
    String valueType = MobileControlValueTypes.integer,
    bool emitOnUpdateConfig = false,
  }) {
    return <String, dynamic>{
      'controlId': controlId,
      'type': MobileControlTypes.select,
      'label': label,
      'hint': hint,
      'sectionId': sectionId,
      'order': order,
      'defaultValue': defaultValue,
      'binding': <String, dynamic>{
        'target': MobileControlPayloadTargets.gameConfig,
        'path': path,
        'valueType': valueType,
        'emitOnStartGame': true,
        'emitOnUpdateConfig': emitOnUpdateConfig,
      },
      'validation': const <String, dynamic>{'required': true},
      'options': options,
    };
  }

  static Map<String, dynamic> _schemaSliderControl({
    required String controlId,
    required String label,
    required String path,
    required String defaultValue,
    required int order,
    required num minValue,
    required num maxValue,
    required num step,
    String hint = '',
    String sectionId = 'setup',
    String valueType = MobileControlValueTypes.integer,
    bool emitOnUpdateConfig = false,
  }) {
    return <String, dynamic>{
      'controlId': controlId,
      'type': MobileControlTypes.slider,
      'label': label,
      'hint': hint,
      'sectionId': sectionId,
      'order': order,
      'defaultValue': defaultValue,
      'binding': <String, dynamic>{
        'target': MobileControlPayloadTargets.gameConfig,
        'path': path,
        'valueType': valueType,
        'emitOnStartGame': true,
        'emitOnUpdateConfig': emitOnUpdateConfig,
      },
      'validation': <String, dynamic>{
        'required': true,
        'minValue': minValue.toString(),
        'maxValue': maxValue.toString(),
        'step': step.toString(),
      },
    };
  }

  static Map<String, dynamic> _schemaToggleControl({
    required String controlId,
    required String label,
    required String path,
    required String defaultValue,
    required int order,
    String hint = '',
    String sectionId = 'setup',
    bool emitOnUpdateConfig = false,
  }) {
    return <String, dynamic>{
      'controlId': controlId,
      'type': MobileControlTypes.toggle,
      'label': label,
      'hint': hint,
      'sectionId': sectionId,
      'order': order,
      'defaultValue': defaultValue,
      'binding': <String, dynamic>{
        'target': MobileControlPayloadTargets.gameConfig,
        'path': path,
        'valueType': MobileControlValueTypes.boolean,
        'emitOnStartGame': true,
        'emitOnUpdateConfig': emitOnUpdateConfig,
      },
      'validation': const <String, dynamic>{'required': false},
    };
  }

  static List<Map<String, dynamic>> _buildIndexedOptions({
    required int start,
    required int end,
    required String Function(int value) labelBuilder,
  }) {
    return List<Map<String, dynamic>>.generate(
      end - start + 1,
      (index) {
        final value = start + index;
        return _schemaOption(value, labelBuilder(value));
      },
      growable: false,
    );
  }

  static List<Map<String, dynamic>> _buildPiniataLevelOptions() {
    return _buildIndexedOptions(
      start: 0,
      end: 14,
      labelBuilder: (value) {
        if (value < 5) {
          return 'Tutorial ${value + 1}';
        }
        return 'Level ${value - 4}';
      },
    );
  }

  static List<Map<String, dynamic>> _buildPiniataDifficultyOptions() {
    return const <Map<String, dynamic>>[
      {'value': '0', 'label': 'Very Easy'},
      {'value': '1', 'label': 'Easy'},
      {'value': '2', 'label': 'Medium'},
      {'value': '3', 'label': 'Hard'},
      {'value': '4', 'label': 'Very Hard'},
    ];
  }

  static List<Map<String, dynamic>> _buildButterfliesSpeedOptions() {
    return const <Map<String, dynamic>>[
      {'value': '0', 'label': 'Slow'},
      {'value': '1', 'label': 'Medium'},
      {'value': '2', 'label': 'Fast'},
    ];
  }

  static List<Map<String, dynamic>> _buildCodingOptions() {
    return const <Map<String, dynamic>>[
      {'value': '0', 'label': 'Numbers'},
      {'value': '1', 'label': 'Morse'},
      {'value': '2', 'label': 'Piano'},
    ];
  }

  static List<Map<String, dynamic>> _buildHidingOptions() {
    return const <Map<String, dynamic>>[
      {'value': '0', 'label': 'Pre-test'},
      {'value': '1', 'label': 'Training'},
      {'value': '2', 'label': 'Post-test'},
    ];
  }

  static List<Map<String, dynamic>> _buildActiveMindfulnessOptions() {
    return const <Map<String, dynamic>>[
      {'value': '0', 'label': 'Wall 1'},
      {'value': '1', 'label': 'Wall 2'},
      {'value': '2', 'label': 'Wall 3'},
      {'value': '3', 'label': 'Wall 4'},
    ];
  }

  static List<Map<String, dynamic>> _buildPuzzleOptions() {
    return const <Map<String, dynamic>>[
      {'value': '0', 'label': 'Puzzle 1 - 3x4'},
      {'value': '1', 'label': 'Puzzle 2 - 3x4'},
      {'value': '2', 'label': 'Puzzle 3 - 4x5'},
      {'value': '3', 'label': 'Puzzle 4 - 4x5'},
      {'value': '4', 'label': 'Puzzle 5 - 5x5'},
      {'value': '5', 'label': 'Puzzle 6 - 5x5'},
      {'value': '6', 'label': 'Puzzle 7 - 5x6'},
      {'value': '7', 'label': 'Puzzle 8 - 5x6'},
      {'value': '8', 'label': 'Puzzle 9 - 6x6'},
      {'value': '9', 'label': 'Puzzle 10 - 6x6'},
    ];
  }

  static List<Map<String, dynamic>> _buildObjectSetOptions() {
    return const <Map<String, dynamic>>[
      {'value': '0', 'label': 'Christmas'},
      {'value': '1', 'label': 'Arrows'},
      {'value': '2', 'label': 'Squares'},
      {'value': '3', 'label': 'Triangles'},
    ];
  }

  static List<Map<String, dynamic>> _buildSpatialLevelOptions() {
    return _buildIndexedOptions(
      start: 0,
      end: 10,
      labelBuilder: (value) => value == 0 ? 'Tutorial' : 'Level $value',
    );
  }

  static final MobileControlSchemaParseResult _piniataSchemaParse =
      _parseLegacyBundledSchema(
    expectedGameId: 'piniata',
    gameConfigType: 'legacy_piniata_config_v1',
    staticFields: const <String, dynamic>{
      'name': 'Piniata',
      'code': 'Piniata',
      'sessionSecondsTime': 0,
      'wrongHits': 0,
      'averageReactionTime': 0,
      'maxPoints': 0,
      'locale': r'$deviceLocale',
    },
    controls: <Map<String, dynamic>>[
      _schemaSelectControl(
        controlId: 'piniata_level',
        label: 'Level',
        path: 'level',
        defaultValue: '0',
        order: 10,
        options: _buildPiniataLevelOptions(),
      ),
      _schemaSelectControl(
        controlId: 'piniata_difficulty',
        label: 'Difficulty',
        path: 'difficultyLevel',
        defaultValue: '0',
        order: 20,
        options: _buildPiniataDifficultyOptions(),
      ),
    ],
  );

  static final MobileControlSchemaParseResult _butterfliesSchemaParse =
      _parseLegacyBundledSchema(
    expectedGameId: 'butterflies',
    gameConfigType: 'legacy_butterflies_config_v1',
    staticFields: const <String, dynamic>{
      'name': 'Butterflies',
      'code': 'Butterflies',
      'sessionSecondsTime': 0,
      'wrongPoints': 0,
      'locale': r'$deviceLocale',
    },
    controls: <Map<String, dynamic>>[
      _schemaSelectControl(
        controlId: 'butterflies_level',
        label: 'Level',
        path: 'level',
        defaultValue: '1',
        order: 10,
        options: _buildIndexedOptions(
          start: 1,
          end: 12,
          labelBuilder: (value) => 'Level $value',
        ),
      ),
      _schemaSelectControl(
        controlId: 'butterflies_speed',
        label: 'Speed',
        path: 'butterfliesSpeed',
        defaultValue: '0',
        order: 20,
        options: _buildButterfliesSpeedOptions(),
      ),
      _schemaSliderControl(
        controlId: 'butterflies_count',
        label: 'Butterflies Count',
        path: 'butterfliesCount',
        defaultValue: '10',
        order: 30,
        minValue: 8,
        maxValue: 20,
        step: 1,
      ),
    ],
  );

  static final MobileControlSchemaParseResult _codingSchemaParse =
      _parseLegacyBundledSchema(
    expectedGameId: 'coding',
    gameConfigType: 'legacy_coding_config_v1',
    staticFields: const <String, dynamic>{
      'name': 'Coding',
      'code': 'Coding',
      'sessionSecondsTime': 0,
      'currentLevel': 0,
      'levelsProgress': 0,
      'locale': r'$deviceLocale',
    },
    controls: <Map<String, dynamic>>[
      _schemaSelectControl(
        controlId: 'coding_mode',
        label: 'Game Mode',
        path: 'level',
        defaultValue: '0',
        order: 10,
        options: _buildCodingOptions(),
      ),
    ],
  );

  static final MobileControlSchemaParseResult _hidingSchemaParse =
      _parseLegacyBundledSchema(
    expectedGameId: 'hiding_game',
    gameConfigType: 'legacy_hiding_config_v1',
    staticFields: const <String, dynamic>{
      'name': 'Hiding Game',
      'code': 'Hiding',
      'sessionSecondsTime': 0,
      'wrongAnswers': 0,
      'locale': r'$deviceLocale',
    },
    controls: <Map<String, dynamic>>[
      _schemaSelectControl(
        controlId: 'hiding_mode',
        label: 'Mode',
        path: 'level',
        defaultValue: '0',
        order: 10,
        options: _buildHidingOptions(),
      ),
    ],
  );

  static final MobileControlSchemaParseResult _passiveMindfulnessSchemaParse =
      _parseLegacyBundledSchema(
    expectedGameId: 'passive_mindfulness',
    gameConfigType: 'legacy_passive_mindfulness_config_v1',
    staticFields: const <String, dynamic>{
      'name': 'Passive Mindfulness',
      'code': 'PM',
      'locale': r'$deviceLocale',
    },
    controls: <Map<String, dynamic>>[
      _schemaSelectControl(
        controlId: 'passive_session',
        label: 'Session',
        path: 'level',
        defaultValue: '0',
        order: 10,
        options: _buildIndexedOptions(
          start: 0,
          end: 9,
          labelBuilder: (value) => 'Session ${value + 1}',
        ),
      ),
    ],
  );

  static final MobileControlSchemaParseResult _activeMindfulnessSchemaParse =
      _parseLegacyBundledSchema(
    expectedGameId: 'active_mindfulness',
    gameConfigType: 'legacy_active_mindfulness_config_v1',
    staticFields: const <String, dynamic>{
      'name': 'Active Mindfulness',
      'code': 'AM',
      'sessionSecondsTime': 0,
      'currentLevel': 0,
      'levelsProgress': 0,
      'locale': r'$deviceLocale',
    },
    controls: <Map<String, dynamic>>[
      _schemaSelectControl(
        controlId: 'active_wall',
        label: 'Sequence',
        path: 'level',
        defaultValue: '0',
        order: 10,
        options: _buildActiveMindfulnessOptions(),
      ),
    ],
  );

  static final MobileControlSchemaParseResult _puzzleSchemaParse =
      _parseLegacyBundledSchema(
    expectedGameId: 'puzzle',
    gameConfigType: 'legacy_puzzle_config_v1',
    staticFields: const <String, dynamic>{
      'name': 'Puzzle',
      'code': 'Puzzle',
      'puzzleCompletionTime': 0,
      'locale': r'$deviceLocale',
      'hints': <dynamic>[],
      'completedPuzzles': <dynamic>[],
    },
    controls: <Map<String, dynamic>>[
      _schemaSelectControl(
        controlId: 'puzzle_index',
        label: 'Puzzle',
        path: 'puzzleIndex',
        defaultValue: '0',
        order: 10,
        options: _buildPuzzleOptions(),
      ),
    ],
  );

  static final MobileControlSchemaParseResult _bothHandsSchemaParse =
      _parseLegacyBundledSchema(
    expectedGameId: 'both_hands',
    gameConfigType: 'legacy_two_hands_config_v1',
    staticFields: const <String, dynamic>{
      'name': 'Two Hand Manipulation Training',
      'code': 'two_hand_manipulation',
    },
    controls: <Map<String, dynamic>>[
      _schemaSliderControl(
        controlId: 'two_hands_rows',
        label: 'Rows',
        path: 'rows',
        defaultValue: '4',
        order: 10,
        minValue: 1,
        maxValue: 4,
        step: 1,
      ),
      _schemaSliderControl(
        controlId: 'two_hands_repetitions',
        label: 'Rounds',
        path: 'repetitions',
        defaultValue: '3',
        order: 20,
        minValue: 1,
        maxValue: 10,
        step: 1,
      ),
      _schemaToggleControl(
        controlId: 'two_hands_memory_mode',
        label: 'Memory Mode',
        path: 'memoryMode',
        defaultValue: 'false',
        order: 30,
      ),
      _schemaSelectControl(
        controlId: 'two_hands_object_set',
        label: 'Object Set',
        path: 'objectType',
        defaultValue: '0',
        order: 40,
        options: _buildObjectSetOptions(),
      ),
    ],
  );

  static final MobileControlSchemaParseResult _christmasSchemaParse =
      _parseLegacyBundledSchema(
    expectedGameId: 'christmas',
    gameConfigType: 'legacy_christmas_config_v1',
    staticFields: const <String, dynamic>{
      'name': 'Two Hand Manipulation Training',
      'code': 'two_hand_manipulation',
      'objectType': 0,
    },
    controls: <Map<String, dynamic>>[
      _schemaSliderControl(
        controlId: 'christmas_rows',
        label: 'Rows',
        path: 'rows',
        defaultValue: '4',
        order: 10,
        minValue: 1,
        maxValue: 4,
        step: 1,
      ),
      _schemaSliderControl(
        controlId: 'christmas_repetitions',
        label: 'Rounds',
        path: 'repetitions',
        defaultValue: '3',
        order: 20,
        minValue: 1,
        maxValue: 10,
        step: 1,
      ),
      _schemaToggleControl(
        controlId: 'christmas_memory_mode',
        label: 'Memory Mode',
        path: 'memoryMode',
        defaultValue: 'false',
        order: 30,
      ),
    ],
  );

  static final MobileControlSchemaParseResult _spatialSchemaParse =
      _parseLegacyBundledSchema(
    expectedGameId: 'spatial',
    gameConfigType: 'legacy_spatial_config_v1',
    staticFields: const <String, dynamic>{
      'bestTime': 0,
      'results': <dynamic>[],
    },
    controls: <Map<String, dynamic>>[
      _schemaSelectControl(
        controlId: 'spatial_level',
        label: 'Starting Level',
        path: 'studentLevel',
        defaultValue: '0',
        order: 10,
        options: _buildSpatialLevelOptions(),
      ),
    ],
  );

  static final List<_GameCatalogEntry> _fallbackGameCatalog =
      <_GameCatalogEntry>[
    _GameCatalogEntry(
      gameId: 'piniata',
      title: 'Piniata',
      description:
          'A dynamic coordination game focused on reaction time, precision and active full-body play.',
      targetContentVersion: 'build',
      packageUri: '',
      thumbnailUrl:
          'https://firebasestorage.googleapis.com/v0/b/bloc-learning-uni.appspot.com/o/sessions_cover_photos%2FPINIATA-BASE%20FINAL.jpg?alt=media&token=0a1189bb-9720-4e4b-b51c-e7b9ed722bc9',
      supportsSaveResume: false,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 10,
      previewLines: <String>[
        'Visual perception and reaction time.',
        'Physical interaction with moving targets.',
      ],
      mobileControlSchema: _piniataSchemaParse.schema,
      mobileControlSchemaReasonCode: _piniataSchemaParse.reasonCode,
    ),
    _GameCatalogEntry(
      gameId: 'butterflies',
      title: 'Butterfly Catching',
      description:
          'Catch colourful butterflies while training visual attention, accuracy and motor coordination.',
      targetContentVersion: 'build',
      packageUri: '',
      thumbnailUrl:
          'https://firebasestorage.googleapis.com/v0/b/bloc-learning-uni.appspot.com/o/sessions_cover_photos%2FMOTYLE-BAZA%20fin.jpg?alt=media&token=386372bb-21f1-49a8-9941-59d12dab3c20',
      supportsSaveResume: false,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 20,
      previewLines: <String>[
        'Fast visual scanning with moving objects.',
        'Eye-hand coordination under time pressure.',
      ],
      mobileControlSchema: _butterfliesSchemaParse.schema,
      mobileControlSchemaReasonCode: _butterfliesSchemaParse.reasonCode,
    ),
    _GameCatalogEntry(
      gameId: 'coding',
      title: 'Coding Game',
      description:
          'A hearing-focused session with number, Morse and sound-based mini-games.',
      targetContentVersion: 'build',
      packageUri: '',
      thumbnailUrl:
          'https://firebasestorage.googleapis.com/v0/b/bloc-learning-uni.appspot.com/o/sessions_cover_photos%2FKodowanie_cover.jpeg?alt=media&token=404e281d-14a6-4a56-b37c-23bd8ee008fb',
      supportsSaveResume: false,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 30,
      previewLines: <String>[
        'Auditory memory and attention.',
        'Three mini-games in one scene.',
      ],
      mobileControlSchema: _codingSchemaParse.schema,
      mobileControlSchemaReasonCode: _codingSchemaParse.reasonCode,
    ),
    _GameCatalogEntry(
      gameId: 'hiding_game',
      title: 'Hearing Games',
      description:
          'Hide-and-seek style listening tasks that support sound localisation and auditory focus.',
      targetContentVersion: 'build',
      packageUri: '',
      thumbnailUrl:
          'https://firebasestorage.googleapis.com/v0/b/bloc-learning-uni.appspot.com/o/sessions_cover_photos%2FIKONA-KUKULKA-MAIN.jpg?alt=media&token=a5bd58ad-e44c-4110-bfc2-cfb1c71e10fa',
      supportsSaveResume: false,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 40,
      previewLines: <String>[
        'Auditory spatial orientation.',
        'Separating key sounds from background noise.',
      ],
      mobileControlSchema: _hidingSchemaParse.schema,
      mobileControlSchemaReasonCode: _hidingSchemaParse.reasonCode,
    ),
    _GameCatalogEntry(
      gameId: 'passive_mindfulness',
      title: 'Passive Mindfulness',
      description:
          'Guided breathing and relaxation sessions designed for calm, low-intensity regulation work.',
      targetContentVersion: 'build',
      packageUri: '',
      thumbnailUrl:
          'https://firebasestorage.googleapis.com/v0/b/bloc-learning-uni.appspot.com/o/sessions_cover_photos%2FPasywnyMindfulness_cover.jpeg?alt=media&token=1df1ccd2-1c55-49e9-b90f-f3c82279b49b',
      supportsSaveResume: true,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 50,
      previewLines: <String>[
        'Breathing support and guided calm.',
        'Best for lower-intensity regulation sessions.',
      ],
      mobileControlSchema: _passiveMindfulnessSchemaParse.schema,
      mobileControlSchemaReasonCode: _passiveMindfulnessSchemaParse.reasonCode,
    ),
    _GameCatalogEntry(
      gameId: 'active_mindfulness',
      title: 'Active Mindfulness',
      description:
          'A more interactive mindfulness scene with memory, attention and regulation tasks.',
      targetContentVersion: 'build',
      packageUri: '',
      thumbnailUrl:
          'https://firebasestorage.googleapis.com/v0/b/bloc-learning-uni.appspot.com/o/sessions_cover_photos%2FAktywnyMindfulness_cover.jpeg?alt=media&token=a01efd02-b811-4b3c-94c4-669c99392f60',
      supportsSaveResume: true,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 60,
      previewLines: <String>[
        'Mindfulness with active tasks.',
        'Attention, memory and regulation combined.',
      ],
      mobileControlSchema: _activeMindfulnessSchemaParse.schema,
      mobileControlSchemaReasonCode: _activeMindfulnessSchemaParse.reasonCode,
    ),
    _GameCatalogEntry(
      gameId: 'puzzle',
      title: 'Puzzle with Niko',
      description:
          'Puzzle-based session built around visual planning, persistence and structured problem solving.',
      targetContentVersion: 'build',
      packageUri: '',
      thumbnailUrl:
          'https://firebasestorage.googleapis.com/v0/b/bloc-learning-uni.appspot.com/o/sessions_cover_photos%2FPUZZLE-fin%20Small.jpg?alt=media&token=ac3ab0e6-ffde-4e2d-aa0e-58e74e851d3f',
      supportsSaveResume: true,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 70,
      previewLines: <String>[
        'Visual planning and sequencing.',
        'Structured challenge with clear goals.',
      ],
      mobileControlSchema: _puzzleSchemaParse.schema,
      mobileControlSchemaReasonCode: _puzzleSchemaParse.reasonCode,
    ),
    _GameCatalogEntry(
      gameId: 'both_hands',
      title: 'Two Hands',
      description:
          'Bilateral interaction tasks that encourage coordinated use of both hands in VR.',
      targetContentVersion: 'build',
      packageUri: '',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 80,
      previewLines: <String>[
        'Bimanual coordination in immersive space.',
        'Designed for mirrored or alternating hand work.',
      ],
      mobileControlSchema: _bothHandsSchemaParse.schema,
      mobileControlSchemaReasonCode: _bothHandsSchemaParse.reasonCode,
    ),
    _GameCatalogEntry(
      gameId: 'spatial',
      title: 'Spatial',
      description:
          'Spatial orientation and placement exercises with table-based interaction.',
      targetContentVersion: 'build',
      packageUri: '',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 90,
      previewLines: <String>[
        'Positioning and orientation in 3D space.',
        'Tabletop tasks with strong spatial cues.',
      ],
      mobileControlSchema: _spatialSchemaParse.schema,
      mobileControlSchemaReasonCode: _spatialSchemaParse.reasonCode,
    ),
    _GameCatalogEntry(
      gameId: 'christmas',
      title: 'Christmas',
      description:
          'Seasonal scene pack with festive interaction, object recognition and playful sensory variety.',
      targetContentVersion: 'build',
      packageUri: '',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 100,
      previewLines: <String>[
        'Festive visuals and object interaction.',
        'A seasonal alternative for lighter sessions.',
      ],
      mobileControlSchema: _christmasSchemaParse.schema,
      mobileControlSchemaReasonCode: _christmasSchemaParse.reasonCode,
    ),
  ];

  static const Set<String> _gameScopedCriticalCommands = <String>{
    CriticalCommandIds.startGame,
    CriticalCommandIds.pauseGame,
    CriticalCommandIds.resumeGame,
    CriticalCommandIds.stopGame,
  };

  static const String _demoCubeGameId = 'demo_cube_clicker';
  static const String _pulseTargetGameId = 'pulse_target_tap';
  static const Set<String> _localBundledAlwaysReadyGameIds = <String>{
    'piniata',
    'butterflies',
    'coding',
    'hiding_game',
    'passive_mindfulness',
    'active_mindfulness',
    'puzzle',
    'both_hands',
    'spatial',
    'christmas',
  };
  static const String _updateConfigCommandId = 'UPDATE_CONFIG';
  static const String _interruptedAutoCloseReasonCode =
      'CORRUPTED_DUAL_TERMINATION';
  static const Duration _recentlyEndedSessionTtl = Duration(seconds: 20);
  static const Duration _discoveryCandidateFreshTtl = Duration(seconds: 12);
  static const Duration _connectionLivenessPollInterval = Duration(seconds: 2);
  static const Duration _connectionSignalGracePeriod = Duration(seconds: 8);
  static const Duration _connectionSignalFallbackTimeout =
      Duration(seconds: 12);
  static const Duration _transientIncidentThrottleWindow = Duration(seconds: 8);
  static const Set<String> _transportFailureReasonCodes = <String>{
    'ACK_TIMEOUT',
    'DISCONNECTED',
    'NO_ACTIVE_TCP_ROUTE',
    'TRANSPORT_CLOSED',
    'SOCKET_CLOSED',
    'TCP_LINK_LOST',
    'NO_RUNTIME_SIGNAL_TIMEOUT',
    'RUNTIME_SIGNAL_STALE',
    'TCP_CLIENT_DISCONNECTED',
  };
  static const Set<String> _headsetUnavailableReasonCodes = <String>{
    'APP_BACKGROUND',
    'APP_FOCUS_LOST',
    'APP_QUIT',
  };
  static const bool _rewardsUnlocksTemporarilyDisabled = true;
  static const double _timelineDrawerCollapsedRailWidth = 5;
  static const double _timelineDrawerHandleWidth = 28;
  static const double _timelineDrawerHandleHeight = 96;
  static const double _timelineDrawerHandleHitWidth = 36;
  static final Map<String, DateTime> _recentlyEndedSessionIds =
      <String, DateTime>{};
  static const List<String> _demoLevelModes = <String>[
    'basic',
    'alternate_colors',
    'random_target_color',
  ];

  final ConnectionService _connection = ConnectionService();
  late final GameDataService _gameDataService;
  late final OperatorIncidentPopupQueue _incidentPopupQueue;
  final Set<String> _simulatedOwnedGameIds = <String>{};

  bool _isConnected = false;
  bool _requiresSessionDecision = false;
  bool _isSessionDecisionDialogOpen = false;
  bool _isPrimaryActionInFlight = false;
  bool _allowSystemPop = false;
  bool _contentSyncInFlight = false;
  bool _autoReconnectLoopActive = false;
  bool _autoReconnectEnabled = true;
  bool _isVideoPreviewExpanded = false;
  bool _optimisticRuntimeActive = false;
  bool _optimisticRuntimePaused = false;
  bool _sessionAttachInFlight = false;
  bool _sessionAttachReady = false;
  bool _hasConnectedAtLeastOnce = false;
  TherapistSessionSettings _therapistSessionSettings =
      TherapistSessionSettings.defaults();

  SessionLifecycleState? _sessionLifecycleState;
  TherapistRuntimeStatus? _runtimeStatus;
  final Map<String, PurchasedContentState> _contentStatesByGameId =
      <String, PurchasedContentState>{};
  final Map<String, PackageProbeResultSignal> _latestPackageProbeByGameId =
      <String, PackageProbeResultSignal>{};
  final Set<String> _contentActionsInFlight = <String>{};
  final Set<String> _questReportedContentGameIds = <String>{};

  StreamSubscription<bool>? _connectionSubscription;
  StreamSubscription<Map<String, dynamic>>? _messageSubscription;
  StreamSubscription<DeviceInfo>? _discoverySubscription;
  Timer? _connectionLivenessTimer;

  late String _activeSessionId;
  late String _selectedGameId;
  String? _remoteSessionIdPendingDecision;
  String? _remoteActiveGameId;
  String? _optimisticPreparedGameId;
  String? _lastSessionStateUpdateSessionId;
  String? _lastRuntimeStatusSessionId;
  TherapySessionRecord? _latestPersistedSession;
  ParentProgressSnapshot _parentProgressSnapshot = ParentProgressSnapshot.empty;
  List<StudentRewardUnlock> _recentRewardUnlocks =
      const <StudentRewardUnlock>[];
  bool _parentInsightsLoading = false;
  bool _persistedSessionRefreshInFlight = false;
  bool _interruptedSessionAutoCloseInFlight = false;
  bool _ownershipConflictRecoveryInFlight = false;
  DeviceInfo? _latestDiscoveryReconnectCandidate;
  DateTime? _latestDiscoveryReconnectSeenAt;
  DateTime? _connectedAtUtc;
  DateTime? _lastConnectionLostAtUtc;
  DateTime? _lastRuntimeSignalAtUtc;
  DevicePresenceUpdateSignal? _lastDevicePresenceSignal;
  int _lastWatchdogStaleAfterMs = 6000;
  bool _livenessRecoveryInFlight = false;
  bool _reconnectGameRuntimeRecoveryPending = false;
  bool _reconnectGameRuntimeRecoveryInFlight = false;
  String? _disconnectReasonOverride;
  String? _reconnectGameRuntimeGameId;
  late final AnimationController _timelineDrawerController;
  final TextEditingController _timelineNoteController = TextEditingController();
  bool _timelineNoteInFlight = false;
  String _loadedJournalSessionId = '';
  List<SessionTimelineEvent> _cachedTimelineEvents =
      const <SessionTimelineEvent>[];
  List<SessionTimelineEvent> _liveTimelinePreviewEvents =
      const <SessionTimelineEvent>[];
  List<GameRunRecord> _cachedGameRuns = const <GameRunRecord>[];
  bool _journalLoading = false;
  bool _journalRefreshPending = false;
  String _pendingJournalSessionId = '';
  String _pendingJournalReason = 'PENDING';
  String? _journalLoadError;
  StreamSubscription<List<SessionTimelineEvent>>?
      _liveTimelinePreviewSubscription;
  String _lastPersistedWorkflowCheckpointFingerprint = '';
  String _lastPersistedDevicePresenceTimelineFingerprint = '';
  String? _deferredHandoffSessionId;
  DateTime? _deferredHandoffMarkedAtUtc;
  String? _deferredHandoffReasonCode;
  int _reconnectLoopEpoch = 0;
  final Map<String, _AppliedGameConfigSnapshot> _appliedConfigBySessionGame =
      <String, _AppliedGameConfigSnapshot>{};
  final Map<String, DateTime> _recentTransientIncidentByFingerprint =
      <String, DateTime>{};
  final Map<String, String> _lastCriticalFailureReasonByCommand =
      <String, String>{};
  int? _lastSentPreviewBitrateKbps;
  MediaPreviewState _mediaPreviewState = MediaPreviewState.initializing;
  DateTime? _lastMediaPreviewStateAtUtc;

  _WorkflowStep _workflowStep = _WorkflowStep.gameCatalog;

  int _demoCubeCount = 12;
  double _demoCubeSpeed = 0.7;
  String _demoLevelMode = _demoLevelModes.first;
  int _pulseTargetCount = 8;
  double _pulseTargetSpeed = 0.7;
  double _pulseTargetScale = 0.3;
  final Map<String, dynamic> _dynamicControlValuesByControlId =
      <String, dynamic>{};
  String _dynamicControlValuesGameId = '';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _timelineDrawerController = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 240),
      value: 0,
    );
    _incidentPopupQueue = OperatorIncidentPopupQueue(
      queueName: 'control_screen',
      languageResolver: () => _therapistSessionSettings.operatorUiLanguage,
    );

    _connection.setDiscoveryService(widget.discoveryService);
    _gameDataService = GameDataService(_connection);
    _gameDataService.onJournalCheckpointCommitted =
        (String sessionId) => _refreshSessionJournal(
              reason: 'GAME_DATA_PERSISTED',
              sessionIdOverride: sessionId,
            );
    _liveTimelinePreviewEvents = _gameDataService.liveTimelinePreviewEvents;
    _liveTimelinePreviewSubscription =
        _gameDataService.liveTimelinePreviewStream.listen((events) {
      if (!mounted) {
        return;
      }
      setState(() {
        _liveTimelinePreviewEvents = events;
      });
    });
    _activeSessionId = _buildLocalSessionId();
    _selectedGameId = '';
    _bootstrapLocalContentStates();

    _setupConnectionListeners();
    _setupDiscoveryListener();
    _startConnectionLivenessWatchdog();
    unawaited(ForegroundServiceBridge.start());
    unawaited(_loadTherapistSessionSettings());
    unawaited(_refreshPersistedSessionSnapshot(triggerPrompt: true));
    unawaited(_refreshParentInsights());
    unawaited(_connect());
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    unawaited(_recordMobileLifecycleEvent(state));
    if (state == AppLifecycleState.resumed) {
      unawaited(_recoverConnectionAfterResume());
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _autoReconnectEnabled = false;
    _timelineDrawerController.dispose();
    _timelineNoteController.dispose();
    _connectionSubscription?.cancel();
    _messageSubscription?.cancel();
    _discoverySubscription?.cancel();
    _connectionLivenessTimer?.cancel();
    _liveTimelinePreviewSubscription?.cancel();
    _gameDataService.onJournalCheckpointCommitted = null;
    unawaited(ForegroundServiceBridge.stop());
    unawaited(_gameDataService.dispose());
    _connection.dispose();
    super.dispose();
  }

  void _setupConnectionListeners() {
    _connectionSubscription = _connection.connectionStatus.listen((connected) {
      if (!mounted) {
        return;
      }

      final wasConnected = _isConnected;
      setState(() {
        _isConnected = connected;
        _questReportedContentGameIds.clear();
        if (!connected) {
          _sessionAttachReady = false;
          _lastSentPreviewBitrateKbps = null;
          _lastDevicePresenceSignal = null;
          _optimisticPreparedGameId = null;
          _mediaPreviewState = MediaPreviewState.waitingForStream;
          _lastMediaPreviewStateAtUtc = DateTime.now().toUtc();
        } else if (!wasConnected) {
          _mediaPreviewState = MediaPreviewState.waitingForStream;
          _lastMediaPreviewStateAtUtc = DateTime.now().toUtc();
        }
      });

      if (connected) {
        _connectedAtUtc = DateTime.now().toUtc();
        _lastRuntimeSignalAtUtc = null;
        _disconnectReasonOverride = null;
        _livenessRecoveryInFlight = false;

        final attachReason =
            _hasConnectedAtLeastOnce ? 'AUTO_RECONNECT' : 'INITIAL_CONNECT';
        _hasConnectedAtLeastOnce = true;

        unawaited(_loadTherapistSessionSettings());
        if (!wasConnected) {
          final connectionEventType = attachReason == 'INITIAL_CONNECT'
              ? 'CONTROLLER_CONNECTED'
              : 'CONTROLLER_RECONNECTED';
          unawaited(
            _recordConnectionLifecycleEvent(
              eventType: connectionEventType,
              reasonCode: attachReason,
            ),
          );
          unawaited(
            _ensureSessionAttached(
              reasonCode: attachReason,
              force: true,
            ),
          );
        }
      } else {
        _connectedAtUtc = null;
        if (wasConnected) {
          _lastConnectionLostAtUtc = DateTime.now().toUtc();
        }
        _lastRuntimeSignalAtUtc = null;
        _livenessRecoveryInFlight = false;
        if (wasConnected) {
          final disconnectReason = _disconnectReasonOverride ?? 'TCP_LINK_LOST';
          _disconnectReasonOverride = null;
          _armReconnectGameRuntimeRecovery(source: disconnectReason);
          unawaited(
            _recordConnectionLifecycleEvent(
              eventType: 'CONTROLLER_DISCONNECTED',
              reasonCode: disconnectReason,
            ),
          );
          // If Quest crashed mid-game, write an interrupted game_run record.
          if (_remoteActiveGameId != null) {
            unawaited(_gameDataService.markActiveRunInterrupted());
          }
        } else {
          _disconnectReasonOverride = null;
        }
        _startAutoReconnectLoop(reason: 'connection_lost');
      }
    });

    _messageSubscription = _connection.messages.listen((message) {
      final previousSessionState = _sessionLifecycleState;
      final sessionUpdate = _filterSessionUpdateByOwnership(
        SessionStateUpdateSignal.tryFromNetworkMessage(message),
      );
      final runtimeUpdate = _filterRuntimeUpdateByOwnership(
        RuntimeStatusUpdateSignal.tryFromNetworkMessage(message),
      );
      final devicePresenceUpdate = _filterDevicePresenceByOwnership(
        DevicePresenceUpdateSignal.tryFromNetworkMessage(message),
      );
      final watchdogHeartbeat = _filterWatchdogHeartbeatByOwnership(
        SessionWatchdogHeartbeatSignal.tryFromNetworkMessage(message),
      );
      final watchdogSessionState = SessionLifecycleState.tryParse(
        watchdogHeartbeat?.sessionState,
      );
      final contentStatusSignal =
          ContentInstallStatusSignal.tryFromNetworkMessage(message);
      final packageProbeSignal =
          PackageProbeResultSignal.tryFromNetworkMessage(message);

      if (mounted && devicePresenceUpdate != null) {
        final previousSignal = _lastDevicePresenceSignal;
        _lastRuntimeSignalAtUtc = DateTime.now().toUtc();

        setState(() {
          _lastDevicePresenceSignal = devicePresenceUpdate;
        });

        _handleDevicePresenceFeedback(
          previousSignal: previousSignal,
          signal: devicePresenceUpdate,
        );
        unawaited(_persistDevicePresenceSignal(devicePresenceUpdate));
      }

      if (mounted &&
          (sessionUpdate != null ||
              runtimeUpdate != null ||
              watchdogHeartbeat != null)) {
        final shouldAdoptCreatedSessionRollover = sessionUpdate != null &&
            sessionUpdate.state == SessionLifecycleState.created &&
            _shouldAdoptCreatedSessionRollover(
              sessionUpdate.sessionId,
              activeSessionStateOverride: previousSessionState,
            );
        _lastRuntimeSignalAtUtc = DateTime.now().toUtc();
        if (watchdogHeartbeat != null && watchdogHeartbeat.staleAfterMs > 0) {
          _lastWatchdogStaleAfterMs = watchdogHeartbeat.staleAfterMs;
        }
        setState(() {
          if (sessionUpdate != null) {
            if (shouldAdoptCreatedSessionRollover) {
              // Mobile-only arch: Quest's internal session rollover is a
              // complete no-op at the state machine level. The write remaps in
              // _persistRuntimeSessionState and _persistDevicePresenceSignal
              // already route Firestore writes to the correct mobile-* document.
              // Do NOT touch _activeSessionId, _sessionAttachReady, or
              // _sessionLifecycleState — controls must stay active.
              _lastSessionStateUpdateSessionId = sessionUpdate.sessionId;
              debugPrint(
                '[ControlScreen][Ownership] Ignoring rollover CREATED signal (mobile-only arch): '
                'keepActiveSession=$_activeSessionId '
                'incomingSession=${sessionUpdate.sessionId.trim()}',
              );
            } else {
              _lastSessionStateUpdateSessionId = sessionUpdate.sessionId;
              _sessionLifecycleState = sessionUpdate.state;
              if (SessionRecoveryPolicy.isTerminalState(sessionUpdate.state)) {
                _optimisticRuntimeActive = false;
                _optimisticRuntimePaused = false;
                _remoteActiveGameId = null;
                _lastDevicePresenceSignal = _clearActiveGameFromPresenceSignal(
                  _lastDevicePresenceSignal,
                );
              } else if (sessionUpdate.state ==
                  SessionLifecycleState.inProgress) {
                _optimisticRuntimeActive = true;
                _optimisticRuntimePaused = false;
              } else if (sessionUpdate.state == SessionLifecycleState.paused) {
                _optimisticRuntimeActive = true;
                _optimisticRuntimePaused = true;
              } else if (sessionUpdate.state ==
                  SessionLifecycleState.interrupted) {
                _optimisticRuntimeActive = false;
                _optimisticRuntimePaused = false;
                _lastDevicePresenceSignal = _clearActiveGameFromPresenceSignal(
                  _lastDevicePresenceSignal,
                );
              }
            }
          }

          if (runtimeUpdate != null) {
            _lastRuntimeStatusSessionId = runtimeUpdate.sessionId;
            _runtimeStatus = runtimeUpdate.status;
            switch (runtimeUpdate.status) {
              case TherapistRuntimeStatus.playing:
                _optimisticRuntimeActive = true;
                _optimisticRuntimePaused = false;
                break;
              case TherapistRuntimeStatus.paused:
                _optimisticRuntimeActive = true;
                _optimisticRuntimePaused = true;
                break;
              case TherapistRuntimeStatus.interrupted:
                _optimisticRuntimeActive = false;
                _optimisticRuntimePaused = false;
                _lastDevicePresenceSignal = _clearActiveGameFromPresenceSignal(
                  _lastDevicePresenceSignal,
                );
                break;
              case TherapistRuntimeStatus.syncPending:
                // Sync backlog is not evidence of an unfinished in-progress game.
                break;
              case TherapistRuntimeStatus.connected:
                _optimisticRuntimeActive = false;
                _optimisticRuntimePaused = false;
                _lastDevicePresenceSignal = _clearActiveGameFromPresenceSignal(
                  _lastDevicePresenceSignal,
                );
                break;
            }
          }

          if (watchdogHeartbeat != null) {
            final activeGameId = watchdogHeartbeat.activeGameId.trim();
            if (activeGameId.isNotEmpty) {
              _remoteActiveGameId = activeGameId;
            } else {
              _remoteActiveGameId = null;
              if (sessionUpdate == null && runtimeUpdate == null) {
                _optimisticRuntimeActive = false;
                _optimisticRuntimePaused = false;
              }
            }

            final activeGameState =
                watchdogHeartbeat.activeGameState.trim().toUpperCase();
            if (activeGameState == 'PAUSED') {
              _optimisticRuntimeActive = true;
              _optimisticRuntimePaused = true;
            } else if (activeGameState == 'PLAYING' ||
                activeGameState == 'RUNNING' ||
                activeGameState == 'IN_PROGRESS') {
              _optimisticRuntimeActive = true;
              _optimisticRuntimePaused = false;
            } else if (activeGameState.isNotEmpty &&
                sessionUpdate == null &&
                runtimeUpdate == null) {
              _optimisticRuntimeActive = false;
              _optimisticRuntimePaused = false;
            }

            if (watchdogSessionState != null &&
                SessionRecoveryPolicy.isTerminalState(watchdogSessionState)) {
              _optimisticRuntimeActive = false;
              _optimisticRuntimePaused = false;
              _remoteActiveGameId = null;
            }
          }
        });

        if (sessionUpdate != null &&
            SessionRecoveryPolicy.isTerminalState(sessionUpdate.state)) {
          _markSessionAsRecentlyEnded(sessionUpdate.sessionId);
        }
        if (watchdogHeartbeat != null &&
            watchdogSessionState != null &&
            SessionRecoveryPolicy.isTerminalState(watchdogSessionState)) {
          _markSessionAsRecentlyEnded(watchdogHeartbeat.sessionId);
        }
        if (sessionUpdate != null) {
          unawaited(_persistRuntimeSessionState(sessionUpdate));
        }

        _handlePotentialSessionDecisionGate(
          sessionUpdate,
          runtimeUpdate,
          watchdogHeartbeat,
        );
        _handleSessionTerminalStateFeedback(
          previousState: previousSessionState,
          sessionUpdate: sessionUpdate,
          runtimeUpdate: runtimeUpdate,
        );
      }

      if (mounted && contentStatusSignal != null) {
        _applyContentInstallStatusSignal(contentStatusSignal);
      }

      if (mounted && packageProbeSignal != null) {
        _applyPackageProbeResultSignal(packageProbeSignal);
      }
    });
  }

  void _startConnectionLivenessWatchdog() {
    _connectionLivenessTimer?.cancel();
    _connectionLivenessTimer = Timer.periodic(
      _connectionLivenessPollInterval,
      (_) => unawaited(_evaluateConnectionLiveness()),
    );
  }

  Future<void> _evaluateConnectionLiveness() async {
    if (!mounted ||
        !_autoReconnectEnabled ||
        !_isConnected ||
        _allowSystemPop ||
        _autoReconnectLoopActive ||
        _isPrimaryActionInFlight ||
        _sessionAttachInFlight ||
        _livenessRecoveryInFlight) {
      return;
    }

    if (!_sessionAttachReady) {
      return;
    }

    final connectedAtUtc = _connectedAtUtc;
    if (connectedAtUtc == null) {
      return;
    }

    final nowUtc = DateTime.now().toUtc();
    if (nowUtc.difference(connectedAtUtc) < _connectionSignalGracePeriod) {
      return;
    }

    final staleAfterMs = _lastWatchdogStaleAfterMs > 0
        ? _lastWatchdogStaleAfterMs
        : _connectionSignalFallbackTimeout.inMilliseconds;
    final adaptiveTimeout = Duration(milliseconds: staleAfterMs + 2500);
    final timeout = adaptiveTimeout < _connectionSignalFallbackTimeout
        ? _connectionSignalFallbackTimeout
        : adaptiveTimeout;

    final lastSignalAtUtc = _lastRuntimeSignalAtUtc;
    if (lastSignalAtUtc == null) {
      if (nowUtc.difference(connectedAtUtc) >= timeout) {
        await _forceLivenessReconnect('NO_RUNTIME_SIGNAL_TIMEOUT');
      }
      return;
    }

    if (nowUtc.difference(lastSignalAtUtc) >= timeout) {
      await _forceLivenessReconnect('RUNTIME_SIGNAL_STALE');
    }
  }

  Future<void> _forceLivenessReconnect(String reasonCode) async {
    if (!mounted ||
        !_isConnected ||
        _allowSystemPop ||
        _livenessRecoveryInFlight) {
      return;
    }

    _livenessRecoveryInFlight = true;
    _disconnectReasonOverride = reasonCode;
    debugPrint(
      '[ControlScreen] Liveness watchdog forcing disconnect ($reasonCode)',
    );

    try {
      await _connection.disconnect();
    } finally {
      _livenessRecoveryInFlight = false;
    }
  }

  void _setupDiscoveryListener() {
    _discoverySubscription = widget.discoveryService.devices.listen((device) {
      if (!_matchesReconnectCandidate(device)) {
        return;
      }

      _latestDiscoveryReconnectCandidate = device;
      _latestDiscoveryReconnectSeenAt = DateTime.now().toUtc();

      if (!mounted || _allowSystemPop || _connection.isConnected) {
        return;
      }

      if (!_autoReconnectLoopActive) {
        _startAutoReconnectLoop(reason: 'discovery_candidate');
      }
    });
  }

  bool _matchesReconnectCandidate(DeviceInfo device) {
    if (device.controlPort != widget.device.controlPort) {
      return false;
    }

    if (device.deviceId == widget.device.deviceId) {
      return true;
    }

    if (device.studentId != null && device.studentId == widget.student.id) {
      return true;
    }

    if (device.deviceName == widget.device.deviceName) {
      return true;
    }

    return false;
  }

  DeviceInfo? _resolveFreshDiscoveryReconnectCandidate() {
    final candidate = _latestDiscoveryReconnectCandidate;
    final seenAt = _latestDiscoveryReconnectSeenAt;
    if (candidate == null || seenAt == null) {
      return null;
    }

    final age = DateTime.now().toUtc().difference(seenAt);
    if (age > _discoveryCandidateFreshTtl) {
      return null;
    }

    return candidate;
  }

  Future<bool> _tryReconnectViaDiscoveryCandidate() async {
    final candidate = _resolveFreshDiscoveryReconnectCandidate();
    if (candidate == null) {
      return false;
    }

    final candidateIp = candidate.ip.trim();
    if (candidateIp.isEmpty) {
      return false;
    }

    final connected = await _connection.connect(
      candidateIp,
      candidate.controlPort,
    );
    if (connected) {
      debugPrint(
        '[ControlScreen] Reconnected via discovery candidate '
        '${candidate.deviceName} @ ${candidate.ip}:${candidate.controlPort}',
      );
    }
    return connected;
  }

  Future<void> _connect() async {
    final success = await _connection.connect(
      widget.device.ip,
      widget.device.controlPort,
    );

    if (!success && mounted) {
      _autoReconnectEnabled = false;
      _enqueueIncidentAlert(
        title: 'Failed to connect to device',
        message:
            'Could not connect to ${widget.device.deviceName} (${widget.device.ip}:${widget.device.controlPort}).',
        reasonCode: 'DISCONNECTED',
      );
      _allowSystemPop = true;
      Navigator.pop(context);
    }
  }

  Future<void> _recoverConnectionAfterResume() async {
    if (!mounted || _connection.isConnected) {
      return;
    }
    _startAutoReconnectLoop(reason: 'app_resumed');
  }

  void _startAutoReconnectLoop({required String reason}) {
    if (!mounted ||
        !_autoReconnectEnabled ||
        _allowSystemPop ||
        _connection.isConnected ||
        _autoReconnectLoopActive) {
      return;
    }

    _autoReconnectLoopActive = true;
    unawaited(_runAutoReconnectLoop(reason: reason));
  }

  Future<void> _runAutoReconnectLoop({required String reason}) async {
    final loopEpoch = ++_reconnectLoopEpoch;
    var loopAttempt = 0;
    while (mounted &&
        _autoReconnectEnabled &&
        !_allowSystemPop &&
        !_connection.isConnected) {
      loopAttempt++;
      await _recordReconnectAttemptEvent(
        loopEpoch: loopEpoch,
        loopAttempt: loopAttempt,
        reasonCode: reason.toUpperCase(),
      );

      // Prefer the freshest discovery endpoint first. This avoids getting
      // stuck on stale IPs that can still accept TCP but never ACK SESSION_ATTACH.
      var ok = await _tryReconnectViaDiscoveryCandidate();
      if (!ok) {
        ok = await _connection.reconnect(
          maxAttempts: 4,
          baseDelay: const Duration(milliseconds: 350),
        );
      }

      if (!mounted || !_autoReconnectEnabled || _allowSystemPop) {
        break;
      }

      if (ok) {
        await _recordReconnectResultEvent(
          loopEpoch: loopEpoch,
          loopAttempt: loopAttempt,
          success: true,
          reasonCode: 'AUTO_RECONNECT',
        );
        if (mounted && _sessionAttachReady) {
          setState(() {
            _sessionAttachReady = false;
          });
        }
        // Keep _isConnected transitions sourced from connectionStatus stream.
        // Otherwise listener-level "wasConnected" detection can be bypassed and
        // SESSION_ATTACH bootstrap may be skipped after reconnect.
        break;
      }

      await _recordReconnectResultEvent(
        loopEpoch: loopEpoch,
        loopAttempt: loopAttempt,
        success: false,
        reasonCode: 'TCP_LINK_LOST',
      );

      if (mounted) {
        setState(() {
          _isConnected = false;
        });
      }

      await Future<void>.delayed(const Duration(seconds: 2));
    }

    _autoReconnectLoopActive = false;
    debugPrint('[ControlScreen] Auto reconnect loop stopped ($reason)');
  }

  String _buildLocalSessionId() {
    return 'mobile-${widget.student.id}-${DateTime.now().toUtc().millisecondsSinceEpoch}';
  }

  String _resolveAttachTargetSessionId() {
    // Mobile-only arch: always prefer mobile-* for SESSION_ATTACH.
    // Quest adopts whatever sessionId we send — sending mobile-* ensures all
    // subsequent Quest signals carry mobile-* in their sessionKey, keeping the
    // ownership filter working correctly and preventing infinite reconnect loops.
    final activeSessionId = _activeSessionId.trim();
    if (activeSessionId.startsWith('mobile-')) {
      return activeSessionId;
    }

    final runtimeSessionId = _lastSessionStateUpdateSessionId?.trim() ?? '';
    if (runtimeSessionId.isNotEmpty) {
      return runtimeSessionId;
    }

    final runtimeStatusSessionId = _lastRuntimeStatusSessionId?.trim() ?? '';
    if (runtimeStatusSessionId.isNotEmpty) {
      return runtimeStatusSessionId;
    }

    final pendingDecisionId = _remoteSessionIdPendingDecision?.trim() ?? '';
    if (pendingDecisionId.isNotEmpty) {
      return pendingDecisionId;
    }

    final persisted = _latestPersistedSession;
    if (persisted != null) {
      final persistedSessionId = persisted.sessionId.trim();
      final persistedIsTerminal = persisted.state != null
          ? SessionRecoveryPolicy.isTerminalState(persisted.state!)
          : persisted.isTerminal;
      if (persistedSessionId.isNotEmpty &&
          !persistedIsTerminal &&
          !_wasSessionRecentlyEnded(persistedSessionId)) {
        return persistedSessionId;
      }
    }

    final generatedSessionId = _buildLocalSessionId();
    _activeSessionId = generatedSessionId;
    return generatedSessionId;
  }

  Future<void> _ensureSessionAttached({
    required String reasonCode,
    bool force = false,
    String? sessionIdOverride,
  }) async {
    if (!mounted || !_isConnected || _allowSystemPop) {
      _logAttachDecision(
        decision: 'SKIP_ATTACH',
        reasonCode: reasonCode,
        sessionId: sessionIdOverride,
      );
      return;
    }
    if (_sessionAttachInFlight) {
      _logAttachDecision(
        decision: 'SKIP_ATTACH_IN_FLIGHT',
        reasonCode: reasonCode,
        sessionId: sessionIdOverride,
      );
      return;
    }
    if (_sessionAttachReady && !force) {
      _logAttachDecision(
        decision: 'SKIP_ALREADY_READY',
        reasonCode: reasonCode,
        sessionId: sessionIdOverride,
      );
      return;
    }

    final targetSessionId =
        (sessionIdOverride ?? _resolveAttachTargetSessionId()).trim();
    if (targetSessionId.isEmpty) {
      _logAttachDecision(
        decision: 'SKIP_EMPTY_SESSION_ID',
        reasonCode: reasonCode,
      );
      return;
    }

    final therapistId = _resolveActorTherapistId();
    final ownerKey = _resolveOwnerKey(therapistIdOverride: therapistId);
    final sessionKey = _resolveSessionKey(
      targetSessionId,
      therapistIdOverride: therapistId,
    );
    final entitlementEvaluatedAtUtc = DateTime.now().toUtc();
    final entitledGameIds =
        _resolveRuntimeEntitledGameIds(entitlementEvaluatedAtUtc);
    final entitlementAccess = EntitlementService.activeAccess;
    _sessionAttachInFlight = true;
    var retryAttachWithEndSessionOverride = false;
    if (mounted) {
      setState(() {});
    }

    try {
      await _recordSessionAttachAttemptEvent(
        sessionId: targetSessionId,
        reasonCode: reasonCode,
      );
      _logAttachDecision(
        decision: 'SEND_SESSION_ATTACH',
        reasonCode: reasonCode,
        commandId: CriticalCommandIds.sessionAttach,
        sessionId: targetSessionId,
      );
      final backendHost = _connection.localEndpointIp.trim();
      await _connection.sendCriticalCommand(
        commandId: CriticalCommandIds.sessionAttach,
        sessionId: targetSessionId,
        payload: <String, dynamic>{
          'sessionId': targetSessionId,
          'studentId': widget.student.id,
          'patientId': widget.student.id,
          'therapistId': therapistId,
          'ownerKey': ownerKey,
          'sessionKey': sessionKey,
          'reasonCode': reasonCode,
          'origin': 'mobile_controller',
          'entitlementProfile': EntitlementService.resolveRuntimeProfileId(),
          'entitlementRole':
              (entitlementAccess?.role ?? EntitlementRole.unknown).wireValue,
          'entitlementPlanTier': (entitlementAccess?.planProfile.tier ??
                  SubscriptionPlanTier.unknown)
              .wireValue,
          'entitlementPolicyVersion': entitlementAccess?.policyVersion ?? '',
          'entitlementSourceTag': entitlementAccess?.sourceTag ?? '',
          'entitlementEvaluatedAtUtc':
              entitlementEvaluatedAtUtc.toIso8601String(),
          'entitledGameIdsCsv': _serializeGameIdsCsv(entitledGameIds),
          'entitledGameIdsCount': entitledGameIds.length,
          'backendHost': backendHost,
          'backendPort': _sessionIngestPort,
          'backendHostSource': 'mobile_local_endpoint',
          'mobileDisconnectBehavior':
              _therapistSessionSettings.mobileDisconnectBehavior.wireValue,
        },
        expiresAtUtc: DateTime.now().toUtc().add(const Duration(seconds: 30)),
        ackTimeout:
            _resolveCriticalCommandAckTimeout(CriticalCommandIds.sessionAttach),
        maxRetries: _resolveCriticalCommandMaxRetries(),
      );

      if (!mounted) {
        return;
      }

      // Mobile-only arch: _activeSessionId is always mobile-* (set by
      // _buildLocalSessionId). targetSessionId may be a Quest UUID used
      // only for the TCP SESSION_ATTACH command above. Do not overwrite a
      // valid mobile-* session ID with the Quest UUID.
      final existingMobileId = _activeSessionId.trim();
      final firestoreSessionId = existingMobileId.startsWith('mobile-')
          ? existingMobileId
          : targetSessionId;
      setState(() {
        _sessionAttachReady = true;
        _activeSessionId = firestoreSessionId;
      });
      _gameDataService.attachSession(
        sessionId: firestoreSessionId,
        studentId: widget.student.id,
        therapistId: therapistId,
      );
      _logAttachDecision(
        decision: 'SESSION_ATTACH_ACK',
        reasonCode: reasonCode,
        commandId: CriticalCommandIds.sessionAttach,
        sessionId: targetSessionId,
      );
      await _recordSessionAttachResultEvent(
        sessionId: targetSessionId,
        reasonCode: reasonCode,
        success: true,
      );

      await _recordConnectionLifecycleEvent(
        eventType: 'SESSION_ATTACH_ACK',
        reasonCode: reasonCode,
        sessionIdOverride: targetSessionId,
      );

      if (_contentDeliveryEnabled) {
        await _syncContentCatalog(silent: true);
      }
      unawaited(
        _syncPreviewBitrateToHeadset(reasonCode: 'SESSION_ATTACH_ACK'),
      );
      if (_reconnectGameRuntimeRecoveryPending) {
        final recoveryGameId = _resolveReconnectGameRuntimeGameId();
        unawaited(
          Future<void>.delayed(const Duration(milliseconds: 2200))
              .then((_) async {
            if (!mounted ||
                !_isConnected ||
                !_sessionAttachReady ||
                !_reconnectGameRuntimeRecoveryPending) {
              return;
            }

            if (_hasRecoveredRuntimeSignalsForGame(recoveryGameId)) {
              _clearReconnectGameRuntimeRecovery(
                reasonCode: 'RUNTIME_ALREADY_RECOVERED_AFTER_ATTACH',
              );
              return;
            }

            final ready = await _ensurePreparedGameRuntimeAfterReconnect(
              trigger: 'SESSION_ATTACH_ACK',
            );
            if (!mounted || !ready) {
              return;
            }

            final gameTitle = _resolveCatalogGameTitle(recoveryGameId);
            final recoveryLabel =
                gameTitle.isEmpty ? 'the selected game' : gameTitle;
            ScaffoldMessenger.of(context).showSnackBar(
              SnackBar(
                content: Text(
                  'Headset restarted. Reloaded $recoveryLabel. Tap Start to continue.',
                ),
                duration: const Duration(seconds: 2),
              ),
            );
          }),
        );
      }
    } catch (e) {
      if (!mounted) {
        return;
      }

      final failureReasonCode =
          OpsErrorCatalog.tryExtractReasonCode(e) ?? reasonCode;
      final attachReasonTag = OpsErrorCatalog.buildReasonTag(failureReasonCode);
      final errorSummary = OpsErrorCatalog.buildOperatorSummary(
        error: e,
        fallbackReasonCode: reasonCode,
      );

      setState(() {
        _sessionAttachReady = false;
      });
      _logAttachDecision(
        decision: 'SESSION_ATTACH_FAILED',
        reasonCode: reasonCode,
        commandId: CriticalCommandIds.sessionAttach,
        sessionId: targetSessionId,
      );
      await _recordSessionAttachResultEvent(
        sessionId: targetSessionId,
        reasonCode: failureReasonCode,
        success: false,
      );

      if (failureReasonCode == 'SESSION_OWNERSHIP_CONFLICT' &&
          reasonCode != 'END_SESSION_OVERRIDE') {
        final recovered = await _tryRecoverSessionOwnershipConflict(
          targetSessionId: targetSessionId,
        );
        if (recovered) {
          retryAttachWithEndSessionOverride = true;
          _logAttachDecision(
            decision: 'SESSION_ATTACH_RECOVERY_OWNERSHIP_OVERRIDE',
            reasonCode: 'END_SESSION_OVERRIDE',
            commandId: CriticalCommandIds.sessionAttach,
            sessionId: targetSessionId,
          );
        }
      }

      if (retryAttachWithEndSessionOverride) {
        return;
      }

      final shouldShowAttachFailurePopup =
          SessionAttachFailurePolicy.shouldShowPopup(
        attachReasonCode: reasonCode,
        failureReasonCode: failureReasonCode,
        isGameSetupWorkflow: _workflowStep == _WorkflowStep.gameSetup,
      );

      if (shouldShowAttachFailurePopup) {
        _enqueueIncidentAlert(
          title: 'Session attach failed',
          message: 'Session attach failed [$attachReasonTag]: $errorSummary',
          reasonCode: failureReasonCode,
          severity: OperatorIncidentSeverity.error,
        );
      } else {
        _logAttachDecision(
          decision: 'SESSION_ATTACH_FAILED_TRANSIENT_SUPPRESSED',
          reasonCode: failureReasonCode,
          commandId: CriticalCommandIds.sessionAttach,
          sessionId: targetSessionId,
        );
      }
    } finally {
      _sessionAttachInFlight = false;
      if (mounted) {
        setState(() {});
      }
      if (retryAttachWithEndSessionOverride && mounted) {
        await _ensureSessionAttached(
          reasonCode: 'END_SESSION_OVERRIDE',
          force: true,
          sessionIdOverride: targetSessionId,
        );
      }
    }
  }

  Future<bool> _tryRecoverSessionOwnershipConflict({
    required String targetSessionId,
  }) async {
    if (!_isConnected || _ownershipConflictRecoveryInFlight) {
      return false;
    }

    _ownershipConflictRecoveryInFlight = true;
    try {
      final ended = await _sendEndSessionWithConfirmation(
        reasonCode: 'END_SESSION_OVERRIDE',
        extraPayload: const <String, dynamic>{
          'reason': 'OwnershipConflictRecovery',
          'reasonCode': 'END_SESSION_OVERRIDE',
        },
      );
      if (!ended) {
        return false;
      }

      if (!mounted) {
        return false;
      }

      setState(() {
        _sessionAttachReady = false;
        // Mobile-only arch guard: only adopt mobile-* IDs.
        // A UUID targetSessionId here means the conflict was with Quest's internal
        // session — keep the existing mobile-* _activeSessionId intact.
        if (targetSessionId.startsWith('mobile-')) {
          _activeSessionId = targetSessionId;
        }
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
      return true;
    } finally {
      _ownershipConflictRecoveryInFlight = false;
    }
  }

  Future<void> _recordConnectionLifecycleEvent({
    required String eventType,
    required String reasonCode,
    String? sessionIdOverride,
  }) async {
    await _appendOperationalTimelineEvent(
      eventType: eventType,
      reasonCode: reasonCode,
      source: 'mobile_controller',
      sessionIdOverride: sessionIdOverride,
    );
  }

  Future<void> _appendOperationalTimelineEvent({
    required String eventType,
    required String reasonCode,
    required String source,
    String? sessionIdOverride,
    DateTime? eventAtUtc,
    String discriminator = '',
    Map<String, dynamic> details = const <String, dynamic>{},
  }) async {
    final normalizedEventType = eventType.trim();
    final normalizedReasonCode = reasonCode.trim();
    final normalizedSource =
        source.trim().isEmpty ? 'mobile_controller' : source.trim();
    final resolvedSessionId = _resolveOperationalTimelineSessionId(
        sessionIdOverride: sessionIdOverride);

    if (normalizedEventType.isEmpty || resolvedSessionId.isEmpty) {
      debugPrint(
        '[ControlScreen] Operational timeline event skipped: '
        'event=$normalizedEventType reason=$normalizedReasonCode '
        'source=$normalizedSource session=$resolvedSessionId',
      );
      return;
    }

    final payloadDetails = <String, dynamic>{
      'reasonCode': normalizedReasonCode,
      'workflowStep': _workflowStep.name,
      'connected': _isConnected,
      'attachReady': _sessionAttachReady,
      ...details,
    };

    if (!_shouldPersistOperationalTimelineEvent(
      eventType: normalizedEventType,
      details: payloadDetails,
    )) {
      debugPrint(
        '[ControlScreen] Operational timeline event kept local-only: '
        'event=$normalizedEventType reason=$normalizedReasonCode '
        'source=$normalizedSource discriminator=$discriminator details=$payloadDetails',
      );
      return;
    }

    final resolvedEventAtUtc = (eventAtUtc ?? DateTime.now().toUtc()).toUtc();
    final timelineEventId = SessionJournalService.buildTimelineEventId(
      sessionId: resolvedSessionId,
      eventType: normalizedEventType,
      source: normalizedSource,
      eventAtUnixMs: resolvedEventAtUtc.millisecondsSinceEpoch,
      details: payloadDetails,
      discriminator: discriminator,
    );

    try {
      await SessionJournalService.appendSessionEvent(
        sessionId: resolvedSessionId,
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        eventType: normalizedEventType,
        gameId: _resolveOperationalTimelineGameId(),
        source: normalizedSource,
        timelineEventId: timelineEventId,
        eventAtUtc: resolvedEventAtUtc,
        details: payloadDetails,
      );
      if (mounted) {
        unawaited(
          _refreshSessionJournal(
            reason: normalizedEventType,
            sessionIdOverride: resolvedSessionId,
          ),
        );
      }
    } catch (e) {
      debugPrint(
        '[ControlScreen] Operational timeline event persist failed: '
        'event=$normalizedEventType reason=$normalizedReasonCode '
        'source=$normalizedSource session=$resolvedSessionId error=$e',
      );
    }
  }

  Future<void> _recordReconnectAttemptEvent({
    required int loopEpoch,
    required int loopAttempt,
    required String reasonCode,
  }) {
    return _appendOperationalTimelineEvent(
      eventType: 'CONTROLLER_RECONNECT_ATTEMPT',
      reasonCode: reasonCode,
      source: 'mobile_controller',
      discriminator: 'epoch_${loopEpoch}_attempt_$loopAttempt',
      details: <String, dynamic>{
        'loopEpoch': loopEpoch,
        'loopAttempt': loopAttempt,
        'connected': _isConnected,
      },
    );
  }

  Future<void> _recordReconnectResultEvent({
    required int loopEpoch,
    required int loopAttempt,
    required bool success,
    required String reasonCode,
  }) {
    return _appendOperationalTimelineEvent(
      eventType: success
          ? 'CONTROLLER_RECONNECT_SUCCESS'
          : 'CONTROLLER_RECONNECT_FAILED',
      reasonCode: reasonCode,
      source: 'mobile_controller',
      discriminator:
          "epoch_${loopEpoch}_attempt_${loopAttempt}_${success ? 'ok' : 'fail'}",
      details: <String, dynamic>{
        'loopEpoch': loopEpoch,
        'loopAttempt': loopAttempt,
        'success': success,
      },
    );
  }

  Future<void> _recordSessionAttachAttemptEvent({
    required String sessionId,
    required String reasonCode,
  }) {
    final nowUtc = DateTime.now().toUtc();
    return _appendOperationalTimelineEvent(
      eventType: 'SESSION_ATTACH_ATTEMPT',
      reasonCode: reasonCode,
      source: 'mobile_controller',
      sessionIdOverride: sessionId,
      eventAtUtc: nowUtc,
      discriminator: 'attempt_${nowUtc.microsecondsSinceEpoch}',
      details: <String, dynamic>{
        'attachReady': _sessionAttachReady,
        'connected': _isConnected,
      },
    );
  }

  Future<void> _recordSessionAttachResultEvent({
    required String sessionId,
    required String reasonCode,
    required bool success,
  }) {
    final nowUtc = DateTime.now().toUtc();
    return _appendOperationalTimelineEvent(
      eventType: success ? 'SESSION_ATTACH_SUCCEEDED' : 'SESSION_ATTACH_FAILED',
      reasonCode: reasonCode,
      source: 'mobile_controller',
      sessionIdOverride: sessionId,
      eventAtUtc: nowUtc,
      discriminator: 'result_${nowUtc.microsecondsSinceEpoch}',
      details: <String, dynamic>{
        'success': success,
      },
    );
  }

  String _resolveOperationalTimelineSessionId({String? sessionIdOverride}) {
    final activeSessionId = _activeSessionId.trim();
    if (activeSessionId.startsWith('mobile-')) {
      return activeSessionId;
    }

    final normalizedOverride = sessionIdOverride?.trim() ?? '';
    if (normalizedOverride.isNotEmpty) {
      return normalizedOverride;
    }

    return _resolveTimelineSessionId().trim();
  }

  String _resolveOperationalTimelineGameId() {
    final remoteActiveGameId = _remoteActiveGameId?.trim() ?? '';
    if (remoteActiveGameId.isNotEmpty) {
      return remoteActiveGameId;
    }

    return _selectedGameId.trim();
  }

  bool _shouldPersistOperationalTimelineEvent({
    required String eventType,
    required Map<String, dynamic> details,
  }) {
    switch (eventType) {
      case 'CONTROLLER_CONNECTED':
      case 'CONTROLLER_RECONNECTED':
      case 'CONTROLLER_DISCONNECTED':
      case 'SESSION_ATTACH_ATTEMPT':
      case 'SESSION_ATTACH_SUCCEEDED':
      case 'SESSION_ATTACH_FAILED':
        return true;
      case 'CONTROLLER_RECONNECT_ATTEMPT':
        return _readOperationalLoopAttempt(details['loopAttempt']) == 1;
      case 'CONTROLLER_RECONNECT_SUCCESS':
      case 'CONTROLLER_RECONNECT_FAILED':
      case 'SESSION_ATTACH_ACK':
        return false;
      default:
        return true;
    }
  }

  int _readOperationalLoopAttempt(dynamic value) {
    if (value is int) {
      return value;
    }
    if (value is num) {
      return value.toInt();
    }
    if (value is String) {
      return int.tryParse(value.trim()) ?? 0;
    }
    return 0;
  }

  Future<void> _persistDevicePresenceSignal(
    DevicePresenceUpdateSignal signal,
  ) async {
    final eventType = _eventTypeForDevicePresenceSignal(signal);
    final resolvedSessionId = _resolveOperationalTimelineSessionId(
      sessionIdOverride: signal.sessionId,
    );
    final normalizedReasonCode = signal.reasonCode.trim().toUpperCase();
    final fingerprint = [
      resolvedSessionId,
      eventType,
      signal.presenceState.wireValue,
      normalizedReasonCode,
    ].join('|');

    if (eventType.isEmpty || resolvedSessionId.isEmpty) {
      debugPrint(
        '[ControlScreen] Device presence kept local-only: '
        'presence=${signal.presenceState.wireValue} reason=${signal.reasonCode} '
        'activeGame=${signal.activeGameId}',
      );
      return;
    }

    if (_lastPersistedDevicePresenceTimelineFingerprint == fingerprint) {
      return;
    }
    _lastPersistedDevicePresenceTimelineFingerprint = fingerprint;

    await _appendOperationalTimelineEvent(
      eventType: eventType,
      reasonCode:
          normalizedReasonCode.isEmpty ? 'UNSPECIFIED' : normalizedReasonCode,
      source: 'quest_runtime',
      sessionIdOverride: signal.sessionId,
      eventAtUtc: signal.changedAtUtc,
      discriminator: fingerprint,
      details: <String, dynamic>{
        'presenceState': signal.presenceState.wireValue,
        'appPaused': signal.appPaused,
        'appFocused': signal.appFocused,
        'hasTcpClient': signal.hasTcpClient,
        'activeGameId': signal.activeGameId.trim(),
        'activeGameState': signal.activeGameState.trim(),
      },
    );
  }

  String _eventTypeForDevicePresenceSignal(DevicePresenceUpdateSignal signal) {
    final normalizedReasonCode = signal.reasonCode.trim().toUpperCase();
    if (normalizedReasonCode == 'TCP_CLIENT_CONNECTED') {
      return 'VR_DEVICE_CONNECTED';
    }

    switch (signal.presenceState) {
      case DevicePresenceState.connected:
        return 'VR_DEVICE_CONNECTED';
      case DevicePresenceState.foreground:
        if (normalizedReasonCode == 'APP_FOCUS_GAINED') {
          return 'VR_FOCUS_GAINED';
        }
        return 'VR_APP_FOREGROUND';
      case DevicePresenceState.background:
        return 'VR_APP_BACKGROUND';
      case DevicePresenceState.focusLost:
        return 'VR_FOCUS_LOST';
      case DevicePresenceState.quitting:
        return 'VR_APP_QUITTING';
    }
  }

  Future<void> _recordMobileLifecycleEvent(AppLifecycleState state) async {
    final reasonCode = () {
      if (state == AppLifecycleState.resumed) {
        return 'APP_RESUMED';
      }
      if (state == AppLifecycleState.inactive) {
        return 'APP_INACTIVE';
      }
      if (state == AppLifecycleState.paused) {
        return 'APP_PAUSED';
      }
      if (state == AppLifecycleState.detached) {
        return 'APP_DETACHED';
      }
      return 'APP_BACKGROUND';
    }();

    if (state == AppLifecycleState.resumed) {
      return;
    }

    await _persistSessionCheckpoint(
      eventType: 'MOBILE_LIFECYCLE_CHECKPOINT',
      reasonCode: reasonCode,
      details: <String, dynamic>{
        'lifecycleState': state.name,
      },
    );
  }

  void _pruneRecentlyEndedSessions() {
    final nowUtc = DateTime.now().toUtc();
    final staleIds = <String>[];
    _recentlyEndedSessionIds.forEach((sessionId, endedAtUtc) {
      if (nowUtc.difference(endedAtUtc) > _recentlyEndedSessionTtl) {
        staleIds.add(sessionId);
      }
    });
    for (final sessionId in staleIds) {
      _recentlyEndedSessionIds.remove(sessionId);
    }
  }

  void _markSessionAsRecentlyEnded(String? sessionId) {
    final normalizedSessionId = sessionId?.trim() ?? '';
    if (normalizedSessionId.isEmpty) {
      return;
    }

    _pruneRecentlyEndedSessions();
    _recentlyEndedSessionIds[normalizedSessionId] = DateTime.now().toUtc();
    _appliedConfigBySessionGame.removeWhere(
      (key, _) => key.startsWith('$normalizedSessionId|'),
    );
  }

  bool _wasSessionRecentlyEnded(String sessionId) {
    _pruneRecentlyEndedSessions();
    return _recentlyEndedSessionIds.containsKey(sessionId);
  }

  void _armReconnectGameRuntimeRecovery({required String source}) {
    final candidateGameId = (_remoteActiveGameId?.trim().isNotEmpty ?? false)
        ? _remoteActiveGameId!.trim()
        : _selectedGameId.trim();
    final shouldRecoverPreparedGame = candidateGameId.isNotEmpty &&
        _isKnownGameId(candidateGameId) &&
        _workflowStep == _WorkflowStep.gameSetup;
    if (!shouldRecoverPreparedGame) {
      return;
    }

    _reconnectGameRuntimeRecoveryPending = true;
    _reconnectGameRuntimeGameId = candidateGameId;
    debugPrint(
      '[ControlScreen] Armed reconnect game runtime recovery: '
      'source=$source game=$candidateGameId',
    );
  }

  void _clearReconnectGameRuntimeRecovery({required String reasonCode}) {
    _reconnectGameRuntimeRecoveryPending = false;
    _reconnectGameRuntimeRecoveryInFlight = false;
    _reconnectGameRuntimeGameId = null;
    debugPrint(
      '[ControlScreen] Cleared reconnect game runtime recovery: '
      'reason=$reasonCode',
    );
  }

  String _resolveReconnectGameRuntimeGameId() {
    final pendingGameId = _reconnectGameRuntimeGameId?.trim() ?? '';
    if (pendingGameId.isNotEmpty) {
      return pendingGameId;
    }

    return _selectedGameId.trim();
  }

  bool _hasFreshRecoveredGameRuntime(String gameId) {
    final normalizedGameId = gameId.trim();
    if (normalizedGameId.isEmpty) {
      return false;
    }

    final devicePresenceGameId =
        _lastDevicePresenceSignal?.activeGameId.trim() ?? '';
    return devicePresenceGameId == normalizedGameId;
  }

  bool _hasRecoveredRuntimeSignalsForGame(String gameId) {
    final normalizedGameId = gameId.trim();
    if (normalizedGameId.isEmpty) {
      return false;
    }

    final presenceGameId = _lastDevicePresenceSignal?.activeGameId.trim() ?? '';
    if (presenceGameId == normalizedGameId) {
      return true;
    }

    final remoteGameId = _remoteActiveGameId?.trim() ?? '';
    if (remoteGameId == normalizedGameId) {
      return true;
    }

    return _runtimeStatus == TherapistRuntimeStatus.playing ||
        _runtimeStatus == TherapistRuntimeStatus.paused ||
        _sessionLifecycleState == SessionLifecycleState.inProgress ||
        _sessionLifecycleState == SessionLifecycleState.paused;
  }

  Future<bool> _ensurePreparedGameRuntimeAfterReconnect({
    required String trigger,
  }) async {
    if (!_reconnectGameRuntimeRecoveryPending) {
      return true;
    }

    while (_reconnectGameRuntimeRecoveryInFlight) {
      await Future<void>.delayed(const Duration(milliseconds: 100));
    }

    if (!_reconnectGameRuntimeRecoveryPending) {
      return true;
    }

    if (!_isConnected || !_sessionAttachReady || _allowSystemPop) {
      return false;
    }

    final recoveryGameId = _resolveReconnectGameRuntimeGameId();
    if (recoveryGameId.isEmpty || !_isKnownGameId(recoveryGameId)) {
      _clearReconnectGameRuntimeRecovery(
          reasonCode: 'RECOVERY_GAME_ID_INVALID');
      return false;
    }

    if (_hasRecoveredRuntimeSignalsForGame(recoveryGameId)) {
      _clearReconnectGameRuntimeRecovery(
        reasonCode: 'RUNTIME_ALREADY_RECOVERED',
      );
      return true;
    }

    if ((_selectedGameId.trim().isEmpty || !_isKnownGameId(_selectedGameId)) &&
        mounted) {
      setState(() {
        _selectedGameId = recoveryGameId;
      });
    }

    _reconnectGameRuntimeRecoveryInFlight = true;
    try {
      await _prepareSelectedGameScene(
        reasonCode: 'RECONNECT_RUNTIME_RECOVERY_$trigger',
        gameIdOverride: recoveryGameId,
      );

      final deadline = DateTime.now().toUtc().add(const Duration(seconds: 5));
      while (DateTime.now().toUtc().isBefore(deadline)) {
        if (!_isConnected || !_sessionAttachReady) {
          return false;
        }
        if (_hasFreshRecoveredGameRuntime(recoveryGameId)) {
          _clearReconnectGameRuntimeRecovery(
            reasonCode: 'RECONNECT_RUNTIME_RECOVERY_READY',
          );
          return true;
        }
        await Future<void>.delayed(const Duration(milliseconds: 150));
      }

      debugPrint(
        '[ControlScreen] Reconnect game runtime recovery timed out: '
        'trigger=$trigger game=$recoveryGameId',
      );
      return false;
    } finally {
      _reconnectGameRuntimeRecoveryInFlight = false;
    }
  }

  bool _isRuntimeStatusNonTerminal(TherapistRuntimeStatus? status) {
    switch (status) {
      case TherapistRuntimeStatus.playing:
      case TherapistRuntimeStatus.paused:
      case TherapistRuntimeStatus.interrupted:
        return true;
      case TherapistRuntimeStatus.syncPending:
      case TherapistRuntimeStatus.connected:
      case null:
        return false;
    }
  }

  bool get _isStableActiveSessionContext {
    return _workflowStep == _WorkflowStep.gameSetup ||
        _isGameRuntimeActive ||
        _isPrimaryActionInFlight;
  }

  bool _isSessionDecisionAllowedByContext() {
    if (_allowSystemPop) {
      return false;
    }

    if (_isStableActiveSessionContext) {
      return false;
    }

    if (!_isConnected) {
      return !_hasConnectedAtLeastOnce;
    }

    if (_sessionAttachInFlight || !_sessionAttachReady) {
      return true;
    }

    if (_workflowStep != _WorkflowStep.gameCatalog) {
      return false;
    }

    return true;
  }

  bool get _hasDeferredHandoff {
    return (_deferredHandoffSessionId?.trim().isNotEmpty ?? false);
  }

  String get _deferredHandoffSessionIdValue {
    return _deferredHandoffSessionId?.trim() ?? '';
  }

  void _markDeferredHandoff({
    required String sessionId,
    required String reasonCode,
    required String source,
  }) {
    final normalizedSessionId = sessionId.trim();
    if (normalizedSessionId.isEmpty ||
        _wasSessionRecentlyEnded(normalizedSessionId)) {
      return;
    }

    final normalizedReasonCode = reasonCode.trim();
    final previousSessionId = _deferredHandoffSessionId?.trim() ?? '';
    final previousReasonCode = _deferredHandoffReasonCode?.trim() ?? '';
    final shouldUpdate = previousSessionId != normalizedSessionId ||
        previousReasonCode != normalizedReasonCode;
    if (!shouldUpdate) {
      return;
    }

    if (mounted) {
      setState(() {
        _deferredHandoffSessionId = normalizedSessionId;
        _deferredHandoffReasonCode = normalizedReasonCode;
        _deferredHandoffMarkedAtUtc = DateTime.now().toUtc();
      });
    } else {
      _deferredHandoffSessionId = normalizedSessionId;
      _deferredHandoffReasonCode = normalizedReasonCode;
      _deferredHandoffMarkedAtUtc = DateTime.now().toUtc();
    }

    _logSessionDecision(
      source: source,
      decision: 'DEFERRED_BADGE_SET',
      sessionId: normalizedSessionId,
      reason: normalizedReasonCode,
    );
    unawaited(
      _recordDeferredHandoffTimelineEvent(
        sessionId: normalizedSessionId,
        eventType: 'SESSION_HANDOFF_DEFERRED',
        reasonCode: normalizedReasonCode,
      ),
    );
  }

  void _clearDeferredHandoff({
    String? sessionId,
    required String source,
    required String reasonCode,
  }) {
    final currentSessionId = _deferredHandoffSessionId?.trim() ?? '';
    if (currentSessionId.isEmpty) {
      return;
    }

    final targetSessionId = sessionId?.trim() ?? '';
    if (targetSessionId.isNotEmpty && targetSessionId != currentSessionId) {
      return;
    }

    if (mounted) {
      setState(() {
        _deferredHandoffSessionId = null;
        _deferredHandoffReasonCode = null;
        _deferredHandoffMarkedAtUtc = null;
      });
    } else {
      _deferredHandoffSessionId = null;
      _deferredHandoffReasonCode = null;
      _deferredHandoffMarkedAtUtc = null;
    }

    _logSessionDecision(
      source: source,
      decision: 'DEFERRED_BADGE_CLEARED',
      sessionId: currentSessionId,
      reason: reasonCode,
    );
    unawaited(
      _recordDeferredHandoffTimelineEvent(
        sessionId: currentSessionId,
        eventType: 'SESSION_HANDOFF_DEFERRED_CLEARED',
        reasonCode: reasonCode,
      ),
    );
  }

  Future<void> _recordDeferredHandoffTimelineEvent({
    required String sessionId,
    required String eventType,
    required String reasonCode,
  }) async {
    try {
      await SessionJournalService.appendSessionEvent(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        eventType: eventType,
        gameId: _selectedGameId,
        details: <String, dynamic>{
          'reasonCode': reasonCode,
          'workflowStep': _workflowStep.name,
          'connected': _isConnected,
          'attachReady': _sessionAttachReady,
        },
      );
    } catch (e) {
      debugPrint(
        '[ControlScreen] Deferred handoff timeline event failed: '
        'session=$sessionId event=$eventType reason=$reasonCode error=$e',
      );
    }
  }

  Future<void> _reviewDeferredHandoff() async {
    final sessionId = _deferredHandoffSessionIdValue;
    if (sessionId.isEmpty) {
      return;
    }

    if (!_isSessionDecisionAllowedByContext()) {
      _logSessionDecision(
        source: 'deferred_badge',
        decision: 'REVIEW_BLOCKED',
        sessionId: sessionId,
        reason: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
      );
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Handoff is still deferred while active game flow is in progress.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (mounted) {
      setState(() {
        _remoteSessionIdPendingDecision = sessionId;
        _requiresSessionDecision = true;
      });
    } else {
      _remoteSessionIdPendingDecision = sessionId;
      _requiresSessionDecision = true;
    }

    _clearDeferredHandoff(
      sessionId: sessionId,
      source: 'deferred_badge',
      reasonCode: 'THERAPIST_REVIEW_REQUESTED',
    );
    _logSessionDecision(
      source: 'deferred_badge',
      decision: 'REVIEW_OPEN_DIALOG',
      sessionId: sessionId,
      reason: 'THERAPIST_REVIEW_REQUESTED',
    );
    _promptSessionDecisionIfNeeded();
  }

  String _describeDeferredHandoffReason(String reasonCode) {
    final normalized = reasonCode.trim();
    switch (normalized) {
      case 'CONTEXT_NOT_ENTRY_OR_RECONNECT':
        return 'active workflow context';
      case 'THERAPIST_KEEP_CURRENT':
        return 'therapist selected Keep current';
      case 'RECOVERY_OVER_WINDOW':
        return 'recovery window exceeded';
      default:
        return normalized.isEmpty ? 'deferred state' : normalized;
    }
  }

  SessionRecoveryEvaluation _evaluateRecoveryWindowState({
    required bool remoteSessionNeedsDecision,
    String? remoteSessionId,
    DateTime? interruptedAtUtc,
  }) {
    DateTime? persistedInterruptedAtUtc = interruptedAtUtc;
    final persisted = _latestPersistedSession;
    if (persistedInterruptedAtUtc == null && persisted != null) {
      final normalizedRemoteSessionId = remoteSessionId?.trim() ?? '';
      final persistedSessionId = persisted.sessionId.trim();
      if (normalizedRemoteSessionId.isEmpty ||
          persistedSessionId == normalizedRemoteSessionId) {
        persistedInterruptedAtUtc = persisted.interruptedAtUtc;
      }
    }

    return SessionRecoveryManager.evaluate(
      remoteSessionNeedsDecision: remoteSessionNeedsDecision,
      nowUtc: DateTime.now().toUtc(),
      interruptedAtUtc: persistedInterruptedAtUtc,
      lastConnectionLostAtUtc: _lastConnectionLostAtUtc,
      sessionRecoveryWindowMinutes:
          _therapistSessionSettings.sessionRecoveryWindowMinutes,
      requireResumeConfirmationAfterRecoveryWindow: _therapistSessionSettings
          .requireResumeConfirmationAfterRecoveryWindow,
    );
  }

  String _recoveryStateReasonCode(SessionRecoveryWindowState state) {
    switch (state) {
      case SessionRecoveryWindowState.interruptedRecoveringUnderWindow:
        return 'RECOVERY_UNDER_WINDOW';
      case SessionRecoveryWindowState
            .interruptedOverWindowNeedsTherapistDecision:
        return 'RECOVERY_OVER_WINDOW';
      case SessionRecoveryWindowState.notApplicable:
        return 'RECOVERY_NOT_APPLICABLE';
    }
  }

  Future<void> _prepareSelectedGameScene({
    required String reasonCode,
    String? gameIdOverride,
  }) async {
    if (!_isConnected || _allowSystemPop) {
      return;
    }

    final selectedGameId = (gameIdOverride ?? _selectedGameId).trim();
    if (selectedGameId.isEmpty || !_isKnownGameId(selectedGameId)) {
      return;
    }

    try {
      debugPrint(
        '[ControlScreen] Sending PREPARE_GAME for game=$selectedGameId '
        'reason=$reasonCode',
      );
      await _connection.sendCommand(
        GameCommandIds.prepareGame,
        <String, dynamic>{'gameId': selectedGameId},
      );
    } catch (e) {
      debugPrint(
        '[ControlScreen] PREPARE_GAME dispatch failed: '
        'game=$selectedGameId reason=$reasonCode error=$e',
      );
    }
  }

  void _logSessionDecision({
    required String source,
    required String decision,
    String? commandId,
    String? sessionId,
    String? reason,
  }) {
    debugPrint(
      '[ControlScreen][SessionGate] source=$source decision=$decision '
      'command=${commandId ?? ''} session=${sessionId ?? ''} '
      'reason=${reason ?? ''} '
      'connected=$_isConnected attachReady=$_sessionAttachReady '
      'attachInFlight=$_sessionAttachInFlight workflow=${_workflowStep.name} '
      'runtimeActive=$_isGameRuntimeActive requiresDecision=$_requiresSessionDecision',
    );
  }

  void _logAttachDecision({
    required String decision,
    required String reasonCode,
    String? commandId,
    String? sessionId,
  }) {
    debugPrint(
      '[ControlScreen][AttachDecision] decision=$decision reason=$reasonCode '
      'command=${commandId ?? ''} session=${sessionId ?? ''} '
      'connected=$_isConnected attachReady=$_sessionAttachReady '
      'attachInFlight=$_sessionAttachInFlight',
    );
  }

  void _enqueueIncidentAlert({
    required String title,
    required String message,
    String reasonCode = '',
    OperatorIncidentSeverity severity = OperatorIncidentSeverity.error,
    String source = 'control_screen',
    Map<String, dynamic> contextData = const <String, dynamic>{},
  }) {
    if (!mounted) {
      return;
    }

    final normalizedReasonCode = reasonCode.trim().toUpperCase();
    final fallbackReasonCode = normalizedReasonCode.isNotEmpty
        ? normalizedReasonCode
        : (OpsErrorCatalog.tryExtractReasonCode(message) ?? 'UNKNOWN');
    final nowUtc = DateTime.now().toUtc();

    if (_isTransportFailureReasonCode(fallbackReasonCode)) {
      final normalizedTitle = title.trim().toUpperCase();
      final normalizedSource = source.trim().toUpperCase();
      final incidentKey =
          '$normalizedSource|$normalizedTitle|$fallbackReasonCode';
      final lastOccurredAtUtc =
          _recentTransientIncidentByFingerprint[incidentKey];
      if (lastOccurredAtUtc != null &&
          nowUtc.difference(lastOccurredAtUtc) <
              _transientIncidentThrottleWindow) {
        return;
      }
      _recentTransientIncidentByFingerprint[incidentKey] = nowUtc;
    }

    _incidentPopupQueue.enqueue(
      context,
      OperatorIncidentAlert(
        occurredAtUtc: nowUtc,
        source: source,
        title: title,
        message: message,
        reasonCode: fallbackReasonCode,
        severity: severity,
        contextData: _buildIncidentContextData(contextData),
      ),
    );
  }

  Map<String, dynamic> _buildIncidentContextData(
    Map<String, dynamic> extras,
  ) {
    final context = <String, dynamic>{
      'screen': 'control_screen',
      'studentId': widget.student.id,
      'studentTherapistId': widget.student.therapistId,
      'deviceId': widget.device.deviceId,
      'deviceName': widget.device.deviceName,
      'deviceIp': widget.device.ip,
      'controlPort': widget.device.controlPort,
      'sessionId': _activeSessionId,
      'selectedGameId': _selectedGameId,
      'connected': _isConnected,
      'sessionAttachReady': _sessionAttachReady,
      'mediaPreviewState': _mediaPreviewState.name,
      'mediaPreviewStateAtUtc':
          _lastMediaPreviewStateAtUtc?.toIso8601String() ?? '',
      'sessionAttachInFlight': _sessionAttachInFlight,
      'requiresSessionDecision': _requiresSessionDecision,
      'workflowStep': _workflowStep.name,
      'runtimeStatus': _runtimeStatus?.wireValue ?? '',
      'sessionState': _sessionLifecycleState?.wireValue ?? '',
      'pendingDecisionSessionId': _remoteSessionIdPendingDecision ?? '',
      'deferredHandoffSessionId': _deferredHandoffSessionId ?? '',
      'deferredHandoffReasonCode': _deferredHandoffReasonCode ?? '',
      'operatorUiLanguage':
          _therapistSessionSettings.operatorUiLanguage.wireValue,
    };
    if (extras.isNotEmpty) {
      context.addAll(extras);
    }
    return context;
  }

  bool _isTransportFailureReasonCode(String reasonCode) {
    final normalized = reasonCode.trim().toUpperCase();
    if (normalized.isEmpty) {
      return false;
    }
    return _transportFailureReasonCodes.contains(normalized);
  }

  bool _shouldApplyLocalEndSessionFallback(String failureReasonCode) {
    if (!_isConnected || _isHeadsetPresenceBlocking) {
      return true;
    }

    final normalized = failureReasonCode.trim().toUpperCase();
    if (normalized.isEmpty) {
      return false;
    }

    return _transportFailureReasonCodes.contains(normalized) ||
        _headsetUnavailableReasonCodes.contains(normalized);
  }

  void _handleMediaPreviewStateChanged(MediaPreviewState state) {
    if (!mounted || _mediaPreviewState == state) {
      return;
    }

    setState(() {
      _mediaPreviewState = state;
      _lastMediaPreviewStateAtUtc = DateTime.now().toUtc();
    });
  }

  String _resolveActorTherapistId() {
    final currentUserId = FirebaseService.currentUser?.uid.trim() ?? '';
    if (currentUserId.isNotEmpty) {
      return currentUserId;
    }
    final studentOwner = widget.student.therapistId.trim();
    if (studentOwner.isNotEmpty) {
      return studentOwner;
    }
    return 'unknown_therapist';
  }

  Future<void> _loadTherapistSessionSettings() async {
    final studentOwnerTherapistId = widget.student.therapistId.trim();
    final settings = _isParentRole && studentOwnerTherapistId.isNotEmpty
        ? await TherapistSessionSettingsService.fetchSettingsForTherapistId(
            studentOwnerTherapistId,
          )
        : await TherapistSessionSettingsService.fetchCurrentTherapistSettings();
    if (!mounted) {
      return;
    }

    setState(() {
      _therapistSessionSettings = settings;
    });
    unawaited(
      _syncPreviewBitrateToHeadset(
        reasonCode: 'THERAPIST_SETTINGS_SYNC',
      ),
    );
  }

  int _resolvePreviewBitrateKbps() {
    final configured = _therapistSessionSettings.previewStreamBitrateKbps;
    if (configured < TherapistSessionSettings.minPreviewStreamBitrateKbps) {
      return TherapistSessionSettings.minPreviewStreamBitrateKbps;
    }
    if (configured > TherapistSessionSettings.maxPreviewStreamBitrateKbps) {
      return TherapistSessionSettings.maxPreviewStreamBitrateKbps;
    }
    return configured;
  }

  Future<void> _syncPreviewBitrateToHeadset({
    required String reasonCode,
    bool force = false,
  }) async {
    if (!_isConnected || !_sessionAttachReady) {
      return;
    }

    final targetKbps = _resolvePreviewBitrateKbps();
    if (!force &&
        _lastSentPreviewBitrateKbps != null &&
        _lastSentPreviewBitrateKbps == targetKbps) {
      return;
    }

    final payload = <String, dynamic>{
      'bitrateKbps': targetKbps,
      'bitrateBps': targetKbps * 1000,
      'origin': 'mobile_settings',
      'reasonCode': reasonCode,
    };

    try {
      await _connection.sendCommand(_setVideoBitrateCommandId, payload);
      _lastSentPreviewBitrateKbps = targetKbps;
      debugPrint(
        '[ControlScreen] Applied preview bitrate override: '
        '${targetKbps}kbps ($reasonCode)',
      );
    } catch (e) {
      _lastSentPreviewBitrateKbps = null;
      if (!mounted) {
        return;
      }
      final summary = OpsErrorCatalog.buildOperatorSummary(
        error: e,
        fallbackReasonCode: 'WEBRTC_VIDEO_BITRATE_SYNC_FAILED',
      );
      _enqueueIncidentAlert(
        title: 'Preview bitrate sync failed',
        message: 'Could not apply preview bitrate: $summary',
        reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ??
            'WEBRTC_VIDEO_BITRATE_SYNC_FAILED',
        severity: OperatorIncidentSeverity.warning,
      );
    }
  }

  String _resolveOwnerKey({String? therapistIdOverride}) {
    return SessionOwnership.ownerKey(
      therapistId: therapistIdOverride ?? _resolveActorTherapistId(),
      studentId: widget.student.id,
    );
  }

  Set<String> _resolveRuntimeEntitledGameIds(DateTime atUtc) {
    final fromEntitlement = EntitlementService.resolveRuntimeEntitledGameIds(
      _effectiveGameCatalog.map((entry) => entry.gameId),
      atUtc: atUtc,
    );
    if (_simulatedOwnedGameIds.isEmpty) {
      return fromEntitlement;
    }
    return {...fromEntitlement, ..._simulatedOwnedGameIds};
  }

  static String _serializeGameIdsCsv(Set<String> gameIds) {
    if (gameIds.isEmpty) {
      return '';
    }

    final sorted = gameIds
        .map((value) => value.trim())
        .where((value) => value.isNotEmpty)
        .toList(growable: false)
      ..sort();
    return sorted.join(',');
  }

  String _resolveSessionKey(
    String sessionId, {
    String? therapistIdOverride,
  }) {
    return SessionOwnership.sessionKey(
      therapistId: therapistIdOverride ?? _resolveActorTherapistId(),
      studentId: widget.student.id,
      sessionId: sessionId,
    );
  }

  bool _shouldAdoptCreatedSessionRollover(
    String incomingSessionId, {
    SessionLifecycleState? activeSessionStateOverride,
  }) {
    return SessionRecoveryPolicy.shouldAdoptCreatedRolloverSession(
      sessionAttachReady: _sessionAttachReady,
      requiresSessionDecision: _requiresSessionDecision,
      activeSessionId: _activeSessionId,
      incomingSessionId: incomingSessionId,
      activeSessionState: activeSessionStateOverride ?? _sessionLifecycleState,
      activeSessionRecentlyEnded: _wasSessionRecentlyEnded(_activeSessionId),
    );
  }

  bool _isSignalOwnershipAccepted({
    required String source,
    required String sessionId,
    required String therapistId,
    required String studentId,
    required String patientId,
    required String ownerKey,
    required String sessionKey,
    bool allowCreatedSessionRollover = false,
  }) {
    final expectedTherapistId = _resolveActorTherapistId().trim();
    final expectedStudentId = widget.student.id.trim();
    final incomingTherapistId = therapistId.trim();
    final incomingStudentId = SessionOwnership.resolveStudentId(
      studentId: studentId,
      patientId: patientId,
    );
    final normalizedSessionId = sessionId.trim();
    final incomingOwnerKeyRaw = ownerKey.trim();
    final incomingSessionKeyRaw = sessionKey.trim();
    final expectedOwnerKey = SessionOwnership.ownerKey(
      therapistId: expectedTherapistId,
      studentId: expectedStudentId,
    );

    if (incomingTherapistId.isEmpty || incomingStudentId.isEmpty) {
      final shouldRejectForMissingOwnership =
          normalizedSessionId.isNotEmpty && _sessionAttachReady;
      if (shouldRejectForMissingOwnership) {
        debugPrint(
          '[ControlScreen][Ownership] Rejecting $source signal due missing '
          'ownership metadata: session=$normalizedSessionId '
          'incomingTherapist=$incomingTherapistId '
          'incomingStudent=$incomingStudentId',
        );
        return false;
      }
      return true;
    }

    final incomingOwnerKey = incomingOwnerKeyRaw.isNotEmpty
        ? incomingOwnerKeyRaw
        : SessionOwnership.ownerKey(
            therapistId: incomingTherapistId,
            studentId: incomingStudentId,
          );
    if (incomingOwnerKey.isEmpty || expectedOwnerKey.isEmpty) {
      if (normalizedSessionId.isNotEmpty && _sessionAttachReady) {
        debugPrint(
          '[ControlScreen][Ownership] Rejecting $source signal due missing '
          'ownerKey: session=$normalizedSessionId '
          'incomingOwner=$incomingOwnerKey expectedOwner=$expectedOwnerKey',
        );
        return false;
      }
      return true;
    }

    if (incomingOwnerKey != expectedOwnerKey) {
      debugPrint(
        '[ControlScreen][Ownership] Rejecting $source signal due owner '
        'mismatch: session=$normalizedSessionId '
        'expected=$expectedOwnerKey incoming=$incomingOwnerKey',
      );
      return false;
    }

    final shouldValidateSessionKey = _sessionAttachReady;
    if (shouldValidateSessionKey && normalizedSessionId.isNotEmpty) {
      final activeSessionId = _activeSessionId.trim();
      if (activeSessionId.isNotEmpty) {
        final expectedSessionKey = SessionOwnership.sessionKey(
          therapistId: expectedTherapistId,
          studentId: expectedStudentId,
          sessionId: activeSessionId,
        );
        final incomingSessionKey = incomingSessionKeyRaw.isNotEmpty
            ? incomingSessionKeyRaw
            : SessionOwnership.sessionKey(
                therapistId: incomingTherapistId,
                studentId: incomingStudentId,
                sessionId: normalizedSessionId,
              );
        if (incomingSessionKey.isEmpty || expectedSessionKey.isEmpty) {
          debugPrint(
            '[ControlScreen][Ownership] Rejecting $source signal due missing '
            'sessionKey: activeSession=$activeSessionId '
            'incomingSession=$normalizedSessionId',
          );
          return false;
        }

        if (incomingSessionKey != expectedSessionKey) {
          if (allowCreatedSessionRollover &&
              _shouldAdoptCreatedSessionRollover(normalizedSessionId)) {
            debugPrint(
              '[ControlScreen][Ownership] Accepting $source signal due '
              'terminal rollover: activeSession=$activeSessionId '
              'incomingSession=$normalizedSessionId',
            );
            return true;
          }
          debugPrint(
            '[ControlScreen][Ownership] Rejecting $source signal due session '
            'mismatch: expectedSessionKey=$expectedSessionKey '
            'incomingSessionKey=$incomingSessionKey',
          );
          return false;
        }
      }
    }

    return true;
  }

  SessionStateUpdateSignal? _filterSessionUpdateByOwnership(
    SessionStateUpdateSignal? signal,
  ) {
    if (signal == null) {
      return null;
    }

    if (!_isSignalOwnershipAccepted(
      source: SessionSignalCommandIds.sessionStateUpdate,
      sessionId: signal.sessionId,
      therapistId: signal.therapistId,
      studentId: signal.studentId,
      patientId: signal.patientId,
      ownerKey: signal.ownerKey,
      sessionKey: signal.sessionKey,
      allowCreatedSessionRollover:
          signal.state == SessionLifecycleState.created,
    )) {
      return null;
    }

    return signal;
  }

  RuntimeStatusUpdateSignal? _filterRuntimeUpdateByOwnership(
    RuntimeStatusUpdateSignal? signal,
  ) {
    if (signal == null) {
      return null;
    }

    if (!_isSignalOwnershipAccepted(
      source: RuntimeStatusSignalCommandIds.runtimeStatusUpdate,
      sessionId: signal.sessionId,
      therapistId: signal.therapistId,
      studentId: signal.studentId,
      patientId: signal.patientId,
      ownerKey: signal.ownerKey,
      sessionKey: signal.sessionKey,
    )) {
      return null;
    }

    return signal;
  }

  DevicePresenceUpdateSignal? _filterDevicePresenceByOwnership(
    DevicePresenceUpdateSignal? signal,
  ) {
    if (signal == null) {
      return null;
    }

    if (!_isSignalOwnershipAccepted(
      source: RuntimeStatusSignalCommandIds.devicePresenceUpdate,
      sessionId: signal.sessionId,
      therapistId: signal.therapistId,
      studentId: signal.studentId,
      patientId: signal.patientId,
      ownerKey: signal.ownerKey,
      sessionKey: signal.sessionKey,
    )) {
      return null;
    }

    return signal;
  }

  SessionWatchdogHeartbeatSignal? _filterWatchdogHeartbeatByOwnership(
    SessionWatchdogHeartbeatSignal? signal,
  ) {
    if (signal == null) {
      return null;
    }

    if (!_isSignalOwnershipAccepted(
      source: RuntimeStatusSignalCommandIds.sessionWatchdogHeartbeat,
      sessionId: signal.sessionId,
      therapistId: signal.therapistId,
      studentId: signal.studentId,
      patientId: signal.patientId,
      ownerKey: signal.ownerKey,
      sessionKey: signal.sessionKey,
    )) {
      return null;
    }

    return signal;
  }

  bool _isKnownGameId(String gameId) {
    final normalizedGameId = gameId.trim();
    if (normalizedGameId.isEmpty) {
      return false;
    }
    for (final entry in _effectiveGameCatalog) {
      if (entry.gameId == normalizedGameId) {
        return true;
      }
    }
    return false;
  }

  Map<String, dynamic> _buildSchemaDrivenGameConfigPayload(
    MobileControlSchema schema, {
    required bool emitForUpdateConfig,
  }) {
    _ensureDynamicControlValuesForSelectedSchema();

    final payload = _resolveSchemaStaticPayloadFields(schema);
    for (final control in schema.controls) {
      if (control.isButton) {
        continue;
      }

      final shouldEmit = emitForUpdateConfig
          ? control.binding.emitOnUpdateConfig
          : control.binding.emitOnStartGame;
      if (!shouldEmit) {
        continue;
      }

      final bindingPath = control.binding.path.trim();
      if (bindingPath.isEmpty) {
        continue;
      }

      var value = _dynamicControlValuesByControlId[control.controlId];
      value ??= _resolveSchemaControlDefaultValue(control);
      payload[bindingPath] =
          _coerceSchemaValue(control.binding.valueType, value);
    }

    if (schema.payload.includeVersionInGameConfig &&
        schema.payload.gameConfigVersion > 0) {
      payload['version'] = schema.payload.gameConfigVersion;
    }

    return payload;
  }

  Map<String, dynamic> _resolveSchemaStaticPayloadFields(
    MobileControlSchema schema,
  ) {
    final resolved =
        _resolveSchemaStaticPayloadValue(schema.payload.staticFields);
    if (resolved is Map<String, dynamic>) {
      return Map<String, dynamic>.from(resolved);
    }
    if (resolved is Map) {
      return resolved.map(
        (key, value) => MapEntry(key.toString(), value),
      );
    }
    return <String, dynamic>{};
  }

  dynamic _resolveSchemaStaticPayloadValue(dynamic value) {
    if (value is String) {
      final normalized = value.trim();
      if (normalized == r'$deviceLocale') {
        return Localizations.maybeLocaleOf(context)?.languageCode ??
            WidgetsBinding.instance.platformDispatcher.locale.languageCode;
      }
      return value;
    }
    if (value is List) {
      return value
          .map<dynamic>((entry) => _resolveSchemaStaticPayloadValue(entry))
          .toList(growable: false);
    }
    if (value is Map) {
      return value.map(
        (key, entry) => MapEntry(
          key.toString(),
          _resolveSchemaStaticPayloadValue(entry),
        ),
      );
    }
    return value;
  }

  String _resolveSchemaGameConfigType(MobileControlSchema schema) {
    final configured = schema.payload.gameConfigType.trim();
    if (configured.isNotEmpty) {
      return configured;
    }

    return '${_selectedGameId}_config';
  }

  int _resolveSchemaGameConfigVersion(MobileControlSchema schema) {
    final version = schema.payload.gameConfigVersion;
    return version > 0 ? version : 1;
  }

  void _ensureDynamicControlValuesForSelectedSchema() {
    final schema = _selectedGameEntry.mobileControlSchema;
    if (schema == null) {
      _dynamicControlValuesByControlId.clear();
      _dynamicControlValuesGameId = '';
      return;
    }

    final normalizedGameId = _selectedGameId.trim();
    if (_dynamicControlValuesGameId != normalizedGameId) {
      _dynamicControlValuesByControlId.clear();
      _dynamicControlValuesGameId = normalizedGameId;
    }

    final knownControlIds = <String>{};
    for (final control in schema.controls) {
      knownControlIds.add(control.controlId);
      _dynamicControlValuesByControlId.putIfAbsent(
        control.controlId,
        () => _resolveSchemaControlDefaultValue(control),
      );
    }

    final staleKeys = _dynamicControlValuesByControlId.keys
        .where((key) => !knownControlIds.contains(key))
        .toList(growable: false);
    for (final key in staleKeys) {
      _dynamicControlValuesByControlId.remove(key);
    }
  }

  dynamic _resolveSchemaControlDefaultValue(MobileControlDefinition control) {
    final raw = control.defaultValue;
    return _coerceSchemaValue(control.binding.valueType, raw);
  }

  dynamic _coerceSchemaValue(String valueType, dynamic raw) {
    switch (valueType) {
      case MobileControlValueTypes.integer:
        if (raw is int) {
          return raw;
        }
        if (raw is num) {
          return raw.round();
        }
        return int.tryParse(raw.toString().trim()) ?? 0;
      case MobileControlValueTypes.decimal:
        if (raw is double) {
          return raw;
        }
        if (raw is num) {
          return raw.toDouble();
        }
        return double.tryParse(raw.toString().trim()) ?? 0.0;
      case MobileControlValueTypes.boolean:
        if (raw is bool) {
          return raw;
        }
        final normalized = raw.toString().trim().toLowerCase();
        return normalized == 'true' || normalized == '1' || normalized == 'yes';
      case MobileControlValueTypes.text:
      default:
        return raw.toString();
    }
  }

  Future<void> _refreshPersistedSessionSnapshot({
    required bool triggerPrompt,
  }) async {
    if (_persistedSessionRefreshInFlight) {
      return;
    }

    _persistedSessionRefreshInFlight = true;
    try {
      final latest = await SessionJournalService.fetchLatestForStudent(
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
      );
      if (!mounted) {
        return;
      }

      setState(() {
        _latestPersistedSession = latest;
      });
      _rehydrateWorkflowFromPersistedSession(latest);
      if (_isParentRole) {
        unawaited(_refreshParentInsights());
      }

      if (triggerPrompt) {
        final handledByAutoClose = _applyInterruptedSessionAutoClose();
        if (!handledByAutoClose) {
          _applyPersistedSessionDecisionGate();
        }
      }
    } catch (e) {
      debugPrint(
        '[ControlScreen] Persisted session snapshot refresh failed: $e',
      );
    } finally {
      _persistedSessionRefreshInFlight = false;
    }
  }

  void _rehydrateWorkflowFromPersistedSession(TherapySessionRecord? persisted) {
    if (persisted == null) {
      return;
    }

    final persistedSessionId = persisted.sessionId.trim();
    if (persistedSessionId.isEmpty) {
      return;
    }

    final persistedGameId = persisted.latestGameId.trim();
    final activeSessionId = _activeSessionId.trim();
    final canAdoptPersistedSession =
        activeSessionId.isEmpty || activeSessionId == persistedSessionId;
    final isCurrentActivePersistedSession =
        activeSessionId.isNotEmpty && activeSessionId == persistedSessionId;
    final shouldAdoptPersistedGameSelection =
        !persisted.requiresHandoffDecision &&
            activeSessionId == persistedSessionId;

    var shouldSetState = false;

    // Do not silently switch local session context to an unfinished remote one.
    // Keep mismatch so decision gate can prompt therapist (resume/start new).
    // Mobile-only arch: never adopt a Quest UUID as _activeSessionId.
    // Only adopt mobile-* session IDs (generated by _buildLocalSessionId).
    //
    // CRITICAL: if we are already attached (_sessionAttachReady == true), do NOT
    // overwrite _activeSessionId — Quest is already sending heartbeats under the
    // current session ID. Overwriting here causes an immediate watchdog mismatch
    // and a spurious disconnect on every first connection.
    if (!persisted.requiresHandoffDecision &&
        !persisted.isTerminal &&
        !_sessionAttachReady &&
        canAdoptPersistedSession &&
        activeSessionId != persistedSessionId &&
        persistedSessionId.startsWith('mobile-')) {
      _activeSessionId = persistedSessionId;
      _sessionAttachReady = false;
      shouldSetState = true;
    }

    if (shouldAdoptPersistedGameSelection &&
        persistedGameId.isNotEmpty &&
        _isKnownGameId(persistedGameId) &&
        _selectedGameId != persistedGameId) {
      _selectedGameId = persistedGameId;
      shouldSetState = true;
    }

    final hasRuntimeOrActionInProgress =
        _isGameRuntimeActive || _isPrimaryActionInFlight;
    // Keep therapist in the current workflow after game completion.
    // Return to catalog should be explicit (back arrow), not automatic.
    final shouldPreferCatalog =
        persisted.requiresHandoffDecision && !isCurrentActivePersistedSession;
    if (shouldPreferCatalog &&
        !hasRuntimeOrActionInProgress &&
        _workflowStep != _WorkflowStep.gameCatalog) {
      _workflowStep = _WorkflowStep.gameCatalog;
      _isVideoPreviewExpanded = false;
      shouldSetState = true;
    }

    if (shouldSetState && mounted) {
      setState(() {});
    }
  }

  SessionLifecycleState _deriveCheckpointSessionState() {
    final sessionId = _resolveTimelineSessionId().trim();
    final persisted = _latestPersistedSession;
    final persistedState =
        persisted != null && persisted.sessionId.trim() == sessionId
            ? persisted.state
            : null;

    if (persistedState != null &&
        SessionRecoveryPolicy.isTerminalState(persistedState)) {
      return persistedState;
    }

    if (_optimisticRuntimePaused ||
        _runtimeStatus == TherapistRuntimeStatus.paused) {
      return SessionLifecycleState.paused;
    }

    if (_isGameRuntimeActive) {
      return SessionLifecycleState.inProgress;
    }

    if (persistedState != null &&
        !SessionRecoveryPolicy.isTerminalState(persistedState)) {
      return persistedState;
    }

    return SessionLifecycleState.created;
  }

  Future<void> _refreshSessionJournal({
    String reason = 'MANUAL',
    String? sessionIdOverride,
  }) async {
    final sessionId = (sessionIdOverride ?? _resolveTimelineSessionId()).trim();
    if (sessionId.isEmpty) {
      if (mounted) {
        setState(() {
          _loadedJournalSessionId = '';
          _cachedTimelineEvents = const <SessionTimelineEvent>[];
          _cachedGameRuns = const <GameRunRecord>[];
          _journalLoading = false;
          _journalLoadError = null;
        });
      }
      return;
    }

    if (_journalLoading && _loadedJournalSessionId == sessionId) {
      _journalRefreshPending = true;
      _pendingJournalSessionId = sessionId;
      _pendingJournalReason = reason;
      return;
    }

    if (mounted) {
      setState(() {
        _loadedJournalSessionId = sessionId;
        _journalLoading = true;
        _journalLoadError = null;
      });
    }

    try {
      final results = await Future.wait<Object>([
        SessionJournalService.fetchSessionTimeline(
          sessionId: sessionId,
          limit: 40,
        ),
        SessionJournalService.fetchGameRuns(sessionId: sessionId),
      ]);

      if (!mounted) {
        return;
      }

      final currentSessionId =
          (sessionIdOverride ?? _resolveTimelineSessionId()).trim();
      if (currentSessionId != sessionId) {
        if (_loadedJournalSessionId == sessionId) {
          setState(() {
            _journalLoading = false;
          });
        }
        return;
      }

      setState(() {
        _cachedTimelineEvents = results[0] as List<SessionTimelineEvent>;
        _cachedGameRuns = results[1] as List<GameRunRecord>;
        _journalLoading = false;
        _journalLoadError = null;
      });
    } catch (e) {
      debugPrint(
        '[ControlScreen] Session journal refresh failed: '
        'reason=$reason session=$sessionId error=$e',
      );
      if (!mounted || _loadedJournalSessionId != sessionId) {
        return;
      }
      final lowerError = e.toString().toLowerCase();
      final suppressPermissionError =
          lowerError.contains('permission-denied') ||
              lowerError.contains('permission denied');
      setState(() {
        _journalLoading = false;
        _journalLoadError = suppressPermissionError ? null : e.toString();
      });
    }

    if (_journalRefreshPending) {
      final pendingSessionId = _pendingJournalSessionId.trim();
      final pendingReason = _pendingJournalReason;
      _journalRefreshPending = false;
      _pendingJournalSessionId = '';
      _pendingJournalReason = 'PENDING';
      if (pendingSessionId.isNotEmpty && mounted) {
        unawaited(
          _refreshSessionJournal(
            reason: pendingReason,
            sessionIdOverride: pendingSessionId,
          ),
        );
      }
    }
  }

  Future<void> _persistSessionCheckpoint({
    required String eventType,
    required String reasonCode,
    _WorkflowStep? workflowStepOverride,
    String? sessionIdOverride,
    String? gameIdOverride,
    Map<String, dynamic> details = const <String, dynamic>{},
    bool includeTimelineEvent = true,
    bool refreshJournalAfterWrite = false,
  }) async {
    final sessionId = (sessionIdOverride ?? _resolveTimelineSessionId()).trim();
    if (sessionId.isEmpty) {
      return;
    }

    final workflowStep = workflowStepOverride ?? _workflowStep;
    final gameId = (gameIdOverride ?? _selectedGameId).trim();
    final fingerprint = [
      sessionId,
      eventType,
      reasonCode,
      workflowStep.name,
      gameId,
    ].join('|');

    if (eventType == 'MOBILE_SCREEN_CHANGED' &&
        _lastPersistedWorkflowCheckpointFingerprint == fingerprint) {
      return;
    }

    try {
      await SessionJournalService.upsertSessionState(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        state: _deriveCheckpointSessionState(),
        latestGameId: gameId,
        reasonCode: reasonCode,
        metadata: <String, dynamic>{
          'origin': 'mobile_checkpoint',
          'workflowStep': workflowStep.name,
          'transportConnected': _isConnected,
          'runtimeStatus': _runtimeStatus?.name ?? '',
          'runtimeActive': _isGameRuntimeActive,
          'selectedGameId': gameId,
        },
      );

      if (includeTimelineEvent) {
        await SessionJournalService.appendSessionEvent(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: _resolveActorTherapistId(),
          eventType: eventType,
          gameId: gameId,
          source: 'mobile_controller',
          details: <String, dynamic>{
            'reasonCode': reasonCode,
            'workflowStep': workflowStep.name,
            ...details,
          },
        );
      }

      if (eventType == 'MOBILE_SCREEN_CHANGED') {
        _lastPersistedWorkflowCheckpointFingerprint = fingerprint;
      }

      if (refreshJournalAfterWrite) {
        await _refreshSessionJournal(
          reason: eventType,
          sessionIdOverride: sessionId,
        );
      }
    } catch (e) {
      debugPrint(
        '[ControlScreen] Session checkpoint persist failed: '
        'event=$eventType reason=$reasonCode session=$sessionId error=$e',
      );
    }
  }

  Future<void> _persistWorkflowCheckpoint({
    required _WorkflowStep workflowStep,
    required String reasonCode,
    String? gameIdOverride,
  }) {
    return _persistSessionCheckpoint(
      eventType: 'MOBILE_SCREEN_CHANGED',
      reasonCode: reasonCode,
      workflowStepOverride: workflowStep,
      gameIdOverride: gameIdOverride,
      details: <String, dynamic>{
        'screen': workflowStep.name,
      },
      refreshJournalAfterWrite: true,
    );
  }

  bool _isLocalSnapshotTerminalState(SessionLifecycleState state) {
    return SessionRecoveryPolicy.isTerminalState(state);
  }

  bool _isLocalSnapshotUnfinishedState(SessionLifecycleState state) {
    switch (state) {
      case SessionLifecycleState.inProgress:
      case SessionLifecycleState.paused:
      case SessionLifecycleState.interrupted:
        return true;
      case SessionLifecycleState.created:
      case SessionLifecycleState.completed:
      case SessionLifecycleState.abortedByTherapist:
      case SessionLifecycleState.failedTechnical:
        return false;
    }
  }

  TherapySessionRecord _buildLocalPersistedSessionSnapshot({
    required String sessionId,
    required SessionLifecycleState state,
    String latestGameId = '',
    String reasonCode = '',
    Map<String, dynamic> metadata = const <String, dynamic>{},
    DateTime? startedAtUtc,
    DateTime? interruptedAtUtc,
    DateTime? endedAtUtc,
    DateTime? updatedAtUtc,
  }) {
    final normalizedSessionId = sessionId.trim();
    final therapistId = _resolveActorTherapistId();
    final studentId = widget.student.id;
    final normalizedGameId = latestGameId.trim();
    final nowUtc = (updatedAtUtc ?? DateTime.now().toUtc()).toUtc();
    final isTerminal = _isLocalSnapshotTerminalState(state);
    final isUnfinished = _isLocalSnapshotUnfinishedState(state);
    final resolvedStartedAtUtc = startedAtUtc ?? nowUtc;

    return TherapySessionRecord(
      sessionId: normalizedSessionId,
      studentId: studentId,
      therapistId: therapistId,
      ownerKey: SessionOwnership.ownerKey(
        therapistId: therapistId,
        studentId: studentId,
      ),
      sessionKey: SessionOwnership.sessionKey(
        therapistId: therapistId,
        studentId: studentId,
        sessionId: normalizedSessionId,
      ),
      stateWire: state.wireValue,
      state: state,
      unfinishedFlag: isUnfinished,
      terminalFlag: isTerminal,
      latestGameId: normalizedGameId,
      reasonCode: reasonCode.trim(),
      startedAtUtc: resolvedStartedAtUtc,
      interruptedAtUtc: state == SessionLifecycleState.interrupted
          ? (interruptedAtUtc ?? nowUtc)
          : interruptedAtUtc,
      endedAtUtc: isTerminal ? (endedAtUtc ?? nowUtc) : endedAtUtc,
      updatedAtUtc: nowUtc,
      updatedAtUnixMs: nowUtc.millisecondsSinceEpoch,
      metadata: Map<String, dynamic>.from(metadata),
    );
  }

  void _setLatestPersistedSessionSnapshotLocally(TherapySessionRecord? record) {
    if (mounted) {
      setState(() {
        _latestPersistedSession = record;
      });
    } else {
      _latestPersistedSession = record;
    }

    _rehydrateWorkflowFromPersistedSession(record);
  }

  Future<void> _refreshParentInsights() async {
    if (!_isParentRole || _parentInsightsLoading) {
      return;
    }

    _parentInsightsLoading = true;
    try {
      final progressSnapshot = _isProgressInsightsAllowed
          ? await ParentProgressService.fetchSnapshot(
              studentId: widget.student.id,
              therapistId: _resolveActorTherapistId(),
            )
          : ParentProgressSnapshot.empty;
      final rewards = _isRewardsUnlocksAllowed
          ? await StudentRewardService.fetchRecentRewardsForStudent(
              studentId: widget.student.id,
            )
          : const <StudentRewardUnlock>[];

      if (!mounted) {
        return;
      }
      setState(() {
        _parentProgressSnapshot = progressSnapshot;
        _recentRewardUnlocks = rewards;
      });
    } catch (e) {
      debugPrint('[ControlScreen] Parent insights refresh failed: $e');
    } finally {
      _parentInsightsLoading = false;
    }
  }

  Future<void> _unlockRewardForCompletedSession({
    required String sessionId,
    required String gameId,
    required String reasonCode,
  }) async {
    if (!_isRewardsUnlocksAllowed) {
      return;
    }

    try {
      final result = await StudentRewardService.unlockForCompletedSession(
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        sessionId: sessionId,
        gameId: gameId,
        reasonCode: reasonCode,
      );
      if (result == null) {
        return;
      }

      if (result.unlockedNow) {
        await SessionJournalService.appendSessionEvent(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: _resolveActorTherapistId(),
          eventType: 'REWARD_UNLOCKED',
          gameId: gameId,
          details: <String, dynamic>{
            'rewardCode': result.reward.rewardCode,
            'rewardTitle': result.reward.rewardTitle,
            'reasonCode': reasonCode,
            'unlockId': result.reward.unlockId,
          },
        );
      }

      await _refreshParentInsights();

      if (result.unlockedNow && mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Reward unlocked: ${result.reward.rewardTitle}'),
            duration: const Duration(seconds: 2),
          ),
        );
      }
    } catch (e) {
      debugPrint(
        '[ControlScreen] Reward unlock persist failed: '
        'session=$sessionId game=$gameId error=$e',
      );
    }
  }

  bool _applyPersistedSessionDecisionGate() {
    final persisted = _latestPersistedSession;
    if (persisted == null) {
      return false;
    }

    final persistedSessionId = persisted.sessionId.trim();
    if (persistedSessionId.isEmpty) {
      return false;
    }

    if (_applyInterruptedSessionAutoClose()) {
      return true;
    }

    if (persisted.requiresHandoffDecision &&
        persistedSessionId != _activeSessionId &&
        !_wasSessionRecentlyEnded(persistedSessionId)) {
      if (!_isSessionDecisionAllowedByContext()) {
        _markDeferredHandoff(
          sessionId: persistedSessionId,
          reasonCode: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
          source: 'persisted',
        );
        _logSessionDecision(
          source: 'persisted',
          decision: 'SUPPRESS',
          sessionId: persistedSessionId,
          reason: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
        );
        if (_requiresSessionDecision &&
            _remoteSessionIdPendingDecision == persistedSessionId) {
          setState(() {
            _requiresSessionDecision = false;
            _remoteSessionIdPendingDecision = null;
          });
        }
        return false;
      }

      final recoveryEvaluation = _evaluateRecoveryWindowState(
        remoteSessionNeedsDecision: true,
        remoteSessionId: persistedSessionId,
        interruptedAtUtc: persisted.interruptedAtUtc,
      );

      if (!mounted) {
        return false;
      }

      _clearDeferredHandoff(
        sessionId: persistedSessionId,
        source: 'persisted',
        reasonCode: 'REVIEW_DIALOG_OPENED',
      );

      setState(() {
        _remoteSessionIdPendingDecision = persistedSessionId;
        _requiresSessionDecision = true;
      });
      _logSessionDecision(
        source: 'persisted',
        decision: 'SHOW_DIALOG',
        sessionId: persistedSessionId,
        reason: _recoveryStateReasonCode(recoveryEvaluation.state),
      );
      _promptSessionDecisionIfNeeded();
      return true;
    }

    if (persistedSessionId == _activeSessionId) {
      _clearDeferredHandoff(
        sessionId: persistedSessionId,
        source: 'persisted',
        reasonCode: 'ACTIVE_SESSION_MATCHED',
      );
    }

    if (!persisted.requiresHandoffDecision) {
      _clearDeferredHandoff(
        sessionId: persistedSessionId,
        source: 'persisted',
        reasonCode: 'PERSISTED_GATE_CLEARED',
      );
    }

    if (!persisted.requiresHandoffDecision &&
        _requiresSessionDecision &&
        _remoteSessionIdPendingDecision == persistedSessionId) {
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
      _logSessionDecision(
        source: 'persisted',
        decision: 'CLEAR_GATE',
        sessionId: persistedSessionId,
        reason: 'PERSISTED_GATE_CLEARED',
      );
      unawaited(
        _ensureSessionAttached(
          reasonCode: 'HANDOFF_GATE_CLEARED',
          force: true,
        ),
      );
      return true;
    }

    return false;
  }

  bool _applyInterruptedSessionAutoClose() {
    final persisted = _latestPersistedSession;
    if (persisted == null || _interruptedSessionAutoCloseInFlight) {
      return false;
    }

    final sessionId = persisted.sessionId.trim();
    if (sessionId.isEmpty || _wasSessionRecentlyEnded(sessionId)) {
      return false;
    }

    final evaluation = SessionRecoveryManager.evaluateInterruptedAutoClose(
      sessionState: persisted.state,
      nowUtc: DateTime.now().toUtc(),
      interruptedAtUtc: persisted.interruptedAtUtc,
      interruptedSessionAutoCloseHours:
          _therapistSessionSettings.interruptedSessionAutoCloseHours,
      autoCloseInterruptedSessionsEnabled:
          _therapistSessionSettings.autoCloseInterruptedSessionsEnabled,
    );
    if (!evaluation.shouldAutoClose) {
      return false;
    }

    _interruptedSessionAutoCloseInFlight = true;
    _logSessionDecision(
      source: 'persisted',
      decision: 'AUTO_CLOSE_INTERRUPTED',
      sessionId: sessionId,
      reason: _interruptedAutoCloseReasonCode,
    );
    unawaited(_autoCloseInterruptedSession(persisted, evaluation));
    return true;
  }

  Future<void> _autoCloseInterruptedSession(
    TherapySessionRecord persisted,
    InterruptedSessionAutoCloseEvaluation evaluation,
  ) async {
    final sessionId = persisted.sessionId.trim();
    if (sessionId.isEmpty) {
      _interruptedSessionAutoCloseInFlight = false;
      return;
    }

    final therapistId = _resolveActorTherapistId();
    final gameId = persisted.latestGameId.trim().isNotEmpty
        ? persisted.latestGameId.trim()
        : _selectedGameId;
    final interruptedAtUtc = persisted.interruptedAtUtc;
    final interruptedAgeMinutes = evaluation.interruptedAge?.inMinutes ?? 0;
    final autoCloseWindowHours = evaluation.autoCloseWindowHours;
    final metadata = <String, dynamic>{
      'origin': 'mobile_auto_close_policy',
      'sourceState': SessionLifecycleState.interrupted.wireValue,
      'autoCloseWindowHours': autoCloseWindowHours,
      'interruptedAtUtc': interruptedAtUtc?.toIso8601String() ?? '',
      'interruptedAgeMinutes': interruptedAgeMinutes,
      'autoCloseState': evaluation.state.name,
    };

    try {
      await SessionJournalService.upsertSessionState(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: therapistId,
        state: SessionLifecycleState.failedTechnical,
        latestGameId: gameId,
        reasonCode: _interruptedAutoCloseReasonCode,
        metadata: metadata,
      );

      _setLatestPersistedSessionSnapshotLocally(
        _buildLocalPersistedSessionSnapshot(
          sessionId: sessionId,
          state: SessionLifecycleState.failedTechnical,
          latestGameId: gameId,
          reasonCode: _interruptedAutoCloseReasonCode,
          metadata: metadata,
          startedAtUtc: persisted.startedAtUtc,
          interruptedAtUtc: interruptedAtUtc,
        ),
      );

      _markSessionAsRecentlyEnded(sessionId);
      _clearDeferredHandoff(
        sessionId: sessionId,
        source: 'persisted',
        reasonCode: _interruptedAutoCloseReasonCode,
      );
      if (mounted) {
        setState(() {
          if (_remoteSessionIdPendingDecision == sessionId) {
            _requiresSessionDecision = false;
            _remoteSessionIdPendingDecision = null;
          }
          if (_activeSessionId.trim() == sessionId) {
            _activeSessionId = _buildLocalSessionId();
            _sessionAttachReady = false;
          }
        });
      }
      _logSessionDecision(
        source: 'persisted',
        decision: 'AUTO_CLOSE_COMPLETED',
        sessionId: sessionId,
        reason: _interruptedAutoCloseReasonCode,
      );
    } catch (e) {
      debugPrint(
        '[ControlScreen] Interrupted session auto-close failed: '
        'session=$sessionId, error=$e',
      );
    } finally {
      _interruptedSessionAutoCloseInFlight = false;
    }
  }

  Future<void> _persistRuntimeSessionState(
    SessionStateUpdateSignal sessionUpdate,
  ) async {
    final sessionId = sessionUpdate.sessionId.trim();
    if (sessionId.isEmpty) {
      return;
    }

    final activeSessionId = _activeSessionId.trim();
    // Remap Quest's UUID session ID to mobile's active session so all
    // Firestore writes land in the correct mobile-* document.
    final resolvedSessionId =
        (activeSessionId.isNotEmpty && sessionId != activeSessionId)
            ? activeSessionId
            : sessionId;
    if (_sessionAttachReady &&
        resolvedSessionId != activeSessionId &&
        !_requiresSessionDecision) {
      debugPrint(
        '[ControlScreen] Ignoring runtime session state from foreign session '
        'while attached: incoming=$sessionId active=$activeSessionId '
        'state=${sessionUpdate.state.wireValue}',
      );
      return;
    }

    final shouldSuppressPlaceholderCreatedState =
        sessionUpdate.state == SessionLifecycleState.created &&
            sessionUpdate.reasonCode.trim().toUpperCase() == 'BEGIN_SESSION' &&
            sessionUpdate.previousState != null &&
            SessionRecoveryPolicy.isTerminalState(sessionUpdate.previousState!);
    if (shouldSuppressPlaceholderCreatedState) {
      debugPrint(
        '[ControlScreen] Suppressing placeholder CREATED rollover signal: '
        'session=$resolvedSessionId previous=${sessionUpdate.previousState?.wireValue ?? ''} '
        'reason=${sessionUpdate.reasonCode}',
      );
      return;
    }

    debugPrint(
      '[ControlScreen] Runtime session state kept local-only: '
      'session=$resolvedSessionId state=${sessionUpdate.state.wireValue} '
      'reason=${sessionUpdate.reasonCode}',
    );
  }

  Future<void> _persistCommandSideEffects(
    String command, {
    String? sessionIdOverride,
    Map<String, dynamic>? extraPayload,
  }) async {
    final sessionId = (sessionIdOverride ?? _activeSessionId).trim();
    if (sessionId.isEmpty) {
      return;
    }

    final therapistId = _resolveActorTherapistId();
    final selectedGameId = _selectedGameId;

    try {
      if (command == CriticalCommandIds.startGame) {
        final startEventDetails = _buildStartGameTimelineDetails(extraPayload);
        final startStateMetadata = <String, dynamic>{
          'origin': 'mobile_command',
          'commandId': CriticalCommandIds.startGame,
          if ((startEventDetails['gameConfigType'] as String? ?? '')
              .trim()
              .isNotEmpty)
            'gameConfigType': startEventDetails['gameConfigType'],
          if (_asTimelineInt(startEventDetails['gameConfigVersion']) > 0)
            'gameConfigVersion': startEventDetails['gameConfigVersion'],
        };

        await SessionJournalService.upsertSessionState(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          state: SessionLifecycleState.inProgress,
          latestGameId: selectedGameId,
          reasonCode: 'START_GAME_SENT',
          metadata: startStateMetadata,
        );
        await SessionJournalService.appendSessionEvent(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          eventType: 'GAME_STARTED',
          gameId: selectedGameId,
          details: startEventDetails,
        );
      } else if (command == CriticalCommandIds.stopGame) {
        await SessionJournalService.upsertSessionState(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          state: SessionLifecycleState.interrupted,
          latestGameId: selectedGameId,
          reasonCode: extraPayload?['reason'] as String? ?? 'USER_EXIT',
          metadata: const <String, dynamic>{'origin': 'mobile_command'},
        );
        await SessionJournalService.appendSessionEvent(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          eventType: 'GAME_ENDED',
          gameId: selectedGameId,
          details: <String, dynamic>{
            'reason': extraPayload?['reason'] as String? ?? 'USER_EXIT',
          },
        );
      } else if (command == CriticalCommandIds.endSession) {
        final endReasonCode = (extraPayload?['reasonCode'] as String? ??
                'THERAPIST_CONFIRMED_END')
            .trim();
        final resolvedEndReasonCode =
            endReasonCode.isEmpty ? 'THERAPIST_CONFIRMED_END' : endReasonCode;
        final shouldAbortSession =
            resolvedEndReasonCode == 'THERAPIST_ABORTED_AFTER_RECOVERY_WINDOW';

        if (shouldAbortSession) {
          await SessionJournalService.upsertSessionState(
            sessionId: sessionId,
            studentId: widget.student.id,
            therapistId: therapistId,
            state: SessionLifecycleState.abortedByTherapist,
            latestGameId: selectedGameId,
            reasonCode: resolvedEndReasonCode,
            metadata: const <String, dynamic>{'origin': 'mobile_command'},
          );
        } else {
          await SessionJournalService.markSessionCompletedByTherapist(
            sessionId: sessionId,
            studentId: widget.student.id,
            therapistId: therapistId,
            latestGameId: selectedGameId,
            reasonCode: resolvedEndReasonCode,
            metadata: const <String, dynamic>{'origin': 'mobile_command'},
          );
        }
        await SessionJournalService.appendSessionEvent(
          sessionId: sessionId,
          studentId: widget.student.id,
          therapistId: therapistId,
          eventType: shouldAbortSession ? 'SESSION_ABORTED' : 'SESSION_ENDED',
          gameId: selectedGameId,
          details: <String, dynamic>{
            'reason': resolvedEndReasonCode,
          },
        );
        if (!shouldAbortSession) {
          await _unlockRewardForCompletedSession(
            sessionId: sessionId,
            gameId: selectedGameId,
            reasonCode: resolvedEndReasonCode,
          );
        }
      }
    } catch (e) {
      debugPrint(
        '[ControlScreen] Persist command side effects failed: command=$command, error=$e',
      );
    } finally {
      if (command == CriticalCommandIds.startGame ||
          command == CriticalCommandIds.pauseGame ||
          command == CriticalCommandIds.resumeGame ||
          command == CriticalCommandIds.stopGame ||
          command == CriticalCommandIds.endSession) {
        await _refreshSessionJournal(
          reason: 'COMMAND_SIDE_EFFECTS',
          sessionIdOverride: sessionId,
        );
      }
    }
  }

  Future<void> _persistStartNewDecisionOutcome({
    required String remoteSessionId,
    required String replacementSessionId,
  }) async {
    final normalizedSessionId = remoteSessionId.trim();
    final normalizedReplacementSessionId = replacementSessionId.trim();
    if (normalizedSessionId.isEmpty) {
      return;
    }

    final therapistId = _resolveActorTherapistId();
    final gameId = _resolveDecisionSessionGameId(normalizedSessionId);
    final metadata = <String, dynamic>{
      'origin': 'mobile_decision_gate',
      'replacementSessionId': normalizedReplacementSessionId,
      'decision': 'START_NEW',
    };
    try {
      await SessionJournalService.markSessionAbortedByTherapist(
        sessionId: normalizedSessionId,
        studentId: widget.student.id,
        therapistId: therapistId,
        latestGameId: gameId,
        reasonCode: 'THERAPIST_ABANDONED_UNFINISHED_SESSION',
        metadata: metadata,
      );

      _setLatestPersistedSessionSnapshotLocally(
        _buildLocalPersistedSessionSnapshot(
          sessionId: normalizedSessionId,
          state: SessionLifecycleState.abortedByTherapist,
          latestGameId: gameId,
          reasonCode: 'THERAPIST_ABANDONED_UNFINISHED_SESSION',
          metadata: metadata,
          startedAtUtc:
              _resolveDecisionSessionRecord(normalizedSessionId)?.startedAtUtc,
          interruptedAtUtc: _resolveDecisionSessionRecord(normalizedSessionId)
              ?.interruptedAtUtc,
        ),
      );
    } catch (e) {
      debugPrint(
        '[ControlScreen] Persist start-new decision outcome failed: session=$normalizedSessionId, error=$e',
      );
    }
  }

  static bool _isDemoCatalogGameId(String gameId) {
    return gameId.trim().toLowerCase() == _demoCubeGameId;
  }

  List<_GameCatalogEntry> get _effectiveGameCatalog {
    final sorted = List<_GameCatalogEntry>.from(_fallbackGameCatalog)
      ..sort((left, right) => left.sortOrder.compareTo(right.sortOrder));
    return List<_GameCatalogEntry>.unmodifiable(sorted);
  }

  bool _isGameOwnedByEntitlement(
    _GameCatalogEntry entry, {
    DateTime? atUtc,
  }) {
    final normalizedGameId = entry.gameId.trim();
    if (normalizedGameId.isEmpty) {
      return false;
    }

    if (_isDemoCatalogGameId(normalizedGameId)) {
      return _simulatedOwnedGameIds.contains(normalizedGameId);
    }

    if (_simulatedOwnedGameIds.contains(normalizedGameId)) {
      return true;
    }

    if (_isLocalBundledAlwaysReadyGame(normalizedGameId)) {
      return true;
    }

    final access = EntitlementService.activeAccess;
    final nowUtc = atUtc ?? DateTime.now().toUtc();
    if (access == null) {
      return false;
    }

    if (entry.requiresExplicitLicense) {
      if (!access.hasAppAccess(nowUtc) ||
          !access.isGameAllowedByPlan(normalizedGameId)) {
        return false;
      }

      final gameGrant = access.gameLicenses[normalizedGameId];
      return gameGrant != null && gameGrant.isActiveAt(nowUtc);
    }

    return EntitlementService.canLaunchGame(normalizedGameId, atUtc: nowUtc);
  }

  List<_GameCatalogEntry> get _entitledGameCatalog {
    return _effectiveGameCatalog;
  }

  _GameCatalogEntry get _selectedGameEntry {
    final catalog = _effectiveGameCatalog;
    final normalizedSelectedGameId = _selectedGameId.trim();
    if (catalog.isNotEmpty) {
      for (final entry in catalog) {
        if (entry.gameId == normalizedSelectedGameId) {
          return entry;
        }
      }

      final fallback = catalog.first;
      if (normalizedSelectedGameId.isNotEmpty &&
          _workflowStep == _WorkflowStep.gameSetup &&
          _selectedGameId != fallback.gameId) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (!mounted || _selectedGameId == fallback.gameId) {
            return;
          }
          setState(() {
            _selectedGameId = fallback.gameId;
          });
        });
      }
      return fallback;
    }

    return _fallbackGameCatalog.first;
  }

  SubscriptionPlanTier get _activePlanTier {
    return EntitlementService.activeAccess?.planProfile.tier ??
        SubscriptionPlanTier.unknown;
  }

  EntitlementRole get _activeRole {
    return EntitlementService.activeAccess?.role ?? EntitlementRole.unknown;
  }

  bool get _isParentRole => _activeRole == EntitlementRole.parent;

  bool get _isFreePlan => _activePlanTier == SubscriptionPlanTier.free;

  bool get _hasRemainingDemoSessions {
    return EntitlementService.hasRemainingDemoSessions();
  }

  bool get _isVrSessionControlAllowed {
    return EntitlementService.isFeatureEnabled(
      EntitlementFeatureKeys.vrSessionControl,
    );
  }

  bool get _isParentGuidedStartAllowed {
    return EntitlementService.isFeatureEnabled(
      EntitlementFeatureKeys.parentGuidedStart,
    );
  }

  bool get _isProgressInsightsAllowed {
    return EntitlementService.isFeatureEnabled(
      EntitlementFeatureKeys.progressInsights,
    );
  }

  bool get _isRewardsUnlocksAllowed {
    return !_rewardsUnlocksTemporarilyDisabled &&
        EntitlementService.isFeatureEnabled(
          EntitlementFeatureKeys.rewardsUnlocks,
        );
  }

  bool get _isPlanBlockingLaunch {
    if (!_isVrSessionControlAllowed) {
      return true;
    }
    if (_isParentRole && !_isParentGuidedStartAllowed) {
      return true;
    }
    if (_isFreePlan && !_hasRemainingDemoSessions) {
      return true;
    }
    return false;
  }

  String? get _planGateBannerText {
    if (!_isVrSessionControlAllowed) {
      return 'Current plan does not allow starting VR sessions.';
    }
    if (_isParentRole && !_isParentGuidedStartAllowed) {
      return 'Current plan does not allow guided parent start.';
    }
    if (_isFreePlan && !_hasRemainingDemoSessions) {
      return 'Free plan demo limit reached. Upgrade plan or reset demo quota.';
    }
    return null;
  }

  bool get _isDemoCubeGameSelected => _selectedGameId == _demoCubeGameId;

  bool get _isPulseTargetGameSelected => _selectedGameId == _pulseTargetGameId;

  bool get _isGameRuntimeActive {
    final presenceGameState =
        _lastDevicePresenceSignal?.activeGameState.trim().toUpperCase() ?? '';
    final inferredFromPresence = presenceGameState == 'PLAYING' ||
        presenceGameState == 'RUNNING' ||
        presenceGameState == 'IN_PROGRESS' ||
        presenceGameState == 'PAUSED';

    return _runtimeStatus == TherapistRuntimeStatus.playing ||
        _runtimeStatus == TherapistRuntimeStatus.paused ||
        _sessionLifecycleState == SessionLifecycleState.inProgress ||
        _sessionLifecycleState == SessionLifecycleState.paused ||
        inferredFromPresence ||
        _optimisticRuntimeActive;
  }

  bool get _isGameRuntimePaused {
    return _runtimeStatus == TherapistRuntimeStatus.paused ||
        _sessionLifecycleState == SessionLifecycleState.paused ||
        _optimisticRuntimePaused;
  }

  bool get _isHeadsetPresenceBlocking {
    if (!_isConnected) {
      return false;
    }

    final state = _lastDevicePresenceSignal?.presenceState;
    return state == DevicePresenceState.background ||
        state == DevicePresenceState.focusLost ||
        state == DevicePresenceState.quitting;
  }

  String? get _headsetPresenceBannerText {
    if (!_isHeadsetPresenceBlocking) {
      return null;
    }

    final signal = _lastDevicePresenceSignal;
    if (signal == null) {
      return null;
    }

    switch (signal.presenceState) {
      case DevicePresenceState.background:
        return 'Headset app is in background/menu (${_describePresenceReason(signal.reasonCode)}). Ask student to return to VR app.';
      case DevicePresenceState.focusLost:
        return 'Headset lost app focus (${_describePresenceReason(signal.reasonCode)}). Controls stay locked until focus returns.';
      case DevicePresenceState.quitting:
        return 'Headset app is closing (${_describePresenceReason(signal.reasonCode)}). Wait for reconnect or reopen app.';
      case DevicePresenceState.connected:
      case DevicePresenceState.foreground:
        return null;
    }
  }

  String _describePresenceReason(String reasonCode) {
    final normalized = reasonCode.trim().toUpperCase();
    if (normalized.isEmpty) {
      return '${OpsErrorCatalog.buildReasonTag(normalized)} unknown reason';
    }

    final reasonTag = OpsErrorCatalog.buildReasonTag(normalized);
    final detail = switch (normalized) {
      'APP_PAUSED' => 'app paused',
      'APP_INACTIVE' => 'app inactive',
      'APP_RESUMED' => 'app resumed',
      'APP_DETACHED' => 'app detached',
      'APP_FOCUS_LOST' => 'focus lost',
      'APP_FOCUS_GAINED' => 'focus regained',
      'APP_QUIT' => 'app quit',
      'TCP_CLIENT_CONNECTED' => 'transport connected',
      _ => normalized,
    };
    return '$reasonTag $detail';
  }

  bool _shouldReturnToCatalogAfterStopReason(String? reasonCode) {
    final normalized = (reasonCode ?? '').trim().toUpperCase();
    return normalized == 'RETURN_TO_MENU' || normalized == 'RETURN_TO_CATALOG';
  }

  bool _shouldKeepPreparedGameAfterStopReason(String? reasonCode) {
    final normalized = (reasonCode ?? '').trim().toUpperCase();
    return normalized == 'THERAPIST_RECONFIGURE' ||
        normalized == 'STOP_ROUND' ||
        normalized == 'STOP_GAME';
  }

  DevicePresenceUpdateSignal? _clearActiveGameFromPresenceSignal(
    DevicePresenceUpdateSignal? signal,
  ) {
    if (signal == null) {
      return null;
    }

    if (signal.activeGameId.trim().isEmpty &&
        signal.activeGameState.trim().isEmpty) {
      return signal;
    }

    return DevicePresenceUpdateSignal(
      sessionId: signal.sessionId,
      studentId: signal.studentId,
      patientId: signal.patientId,
      therapistId: signal.therapistId,
      ownerKey: signal.ownerKey,
      sessionKey: signal.sessionKey,
      presenceState: signal.presenceState,
      reasonCode: signal.reasonCode,
      changedAtUtc: signal.changedAtUtc,
      appPaused: signal.appPaused,
      appFocused: signal.appFocused,
      hasTcpClient: signal.hasTcpClient,
      activeGameId: '',
      activeGameState: '',
    );
  }

  void _handleDevicePresenceFeedback({
    required DevicePresenceUpdateSignal? previousSignal,
    required DevicePresenceUpdateSignal signal,
  }) {
    if (!mounted) {
      return;
    }

    final didStateChange = previousSignal == null ||
        previousSignal.presenceState != signal.presenceState;
    final didReasonChange = previousSignal == null ||
        previousSignal.reasonCode != signal.reasonCode;
    if (!didStateChange && !didReasonChange) {
      return;
    }

    final messenger = ScaffoldMessenger.of(context);
    final reason = _describePresenceReason(signal.reasonCode);

    final isBlockingState =
        signal.presenceState == DevicePresenceState.background ||
            signal.presenceState == DevicePresenceState.focusLost ||
            signal.presenceState == DevicePresenceState.quitting;

    if (isBlockingState) {
      final message = switch (signal.presenceState) {
        DevicePresenceState.background =>
          'Headset opened system menu / app is in background ($reason).',
        DevicePresenceState.focusLost =>
          'Headset focus lost ($reason). Controls are temporarily locked.',
        DevicePresenceState.quitting =>
          'Headset app is closing ($reason). Waiting for reconnect.',
        DevicePresenceState.connected => '',
        DevicePresenceState.foreground => '',
      };

      if (message.isNotEmpty) {
        messenger.showSnackBar(
          SnackBar(
            content: Text(message),
            backgroundColor: Colors.orange.shade800,
            duration: const Duration(seconds: 2),
          ),
        );
      }
      return;
    }

    final wasBlocking = previousSignal != null &&
        (previousSignal.presenceState == DevicePresenceState.background ||
            previousSignal.presenceState == DevicePresenceState.focusLost ||
            previousSignal.presenceState == DevicePresenceState.quitting);
    if (wasBlocking) {
      messenger.showSnackBar(
        const SnackBar(
          content:
              Text('Headset returned to active app. Controls are unlocked.'),
          duration: Duration(seconds: 2),
        ),
      );
    }
  }

  bool get _isSetupLockedByRuntime => _isGameRuntimeActive;

  bool get _isSelectedGamePreparedOnHeadset {
    final selectedGameId = _selectedGameId.trim();
    if (selectedGameId.isEmpty || !_isKnownGameId(selectedGameId)) {
      return false;
    }

    final optimisticPreparedGameId = _optimisticPreparedGameId?.trim() ?? '';
    if (optimisticPreparedGameId == selectedGameId) {
      return true;
    }

    final devicePresenceGameId =
        _lastDevicePresenceSignal?.activeGameId.trim() ?? '';
    if (devicePresenceGameId == selectedGameId) {
      return true;
    }

    final remoteGameId = _remoteActiveGameId?.trim() ?? '';
    return remoteGameId == selectedGameId;
  }

  bool get _isSelectedGamePreparationPending {
    final selectedGameId = _selectedGameId.trim();
    if (!_isConnected ||
        !_sessionAttachReady ||
        _isGameRuntimeActive ||
        selectedGameId.isEmpty ||
        !_isKnownGameId(selectedGameId)) {
      return false;
    }

    return !_isSelectedGamePreparedOnHeadset;
  }

  bool get _isSelectedGameSceneReadyForControls {
    if (_isGameRuntimeActive) {
      return true;
    }

    return _isSelectedGamePreparedOnHeadset;
  }

  String get _selectedGameSceneLoadingHint {
    return 'Quest is still loading the selected game scene. '
        'Settings and game controls unlock when the scene is ready.';
  }

  PurchasedContentState get _selectedContentState {
    return _contentStateForGame(_selectedGameId);
  }

  bool get _isControlLinkReadyForCommands {
    return _isConnected && _sessionAttachReady && !_isHeadsetPresenceBlocking;
  }

  bool get _isPreviewStreamExpected {
    return _workflowStep == _WorkflowStep.gameSetup && _isVideoPreviewExpanded;
  }

  String get _controlLinkBlockedHint {
    if (!_isConnected) {
      return 'Headset is offline.';
    }
    if (_isPreviewStreamExpected &&
        _mediaPreviewState != MediaPreviewState.streaming) {
      return 'VR preview is unavailable.';
    }
    if (!_sessionAttachReady) {
      return 'Session context is still syncing.';
    }
    if (_isHeadsetPresenceBlocking) {
      return 'Headset is not in active VR app yet.';
    }
    return 'Control link is not ready.';
  }

  String get _connectionBadgeLabel {
    if (_isControlLinkReadyForCommands) {
      return 'Connected';
    }
    if (!_isConnected) {
      return 'Reconnecting';
    }
    if (_isPreviewStreamExpected &&
        _mediaPreviewState != MediaPreviewState.streaming) {
      return 'No Preview';
    }
    if (!_sessionAttachReady) {
      return 'Syncing';
    }
    if (_isHeadsetPresenceBlocking) {
      return 'Headset Busy';
    }
    return 'Not Ready';
  }

  IconData get _connectionBadgeIcon {
    if (_isControlLinkReadyForCommands) {
      return Icons.wifi;
    }
    if (!_isConnected) {
      return Icons.wifi_off;
    }
    if (_isPreviewStreamExpected &&
        _mediaPreviewState != MediaPreviewState.streaming) {
      return Icons.wifi_tethering_error_rounded;
    }
    return Icons.sync_problem;
  }

  void _bootstrapLocalContentStates() {
    final nowUtc = DateTime.now().toUtc();
    final catalog = _effectiveGameCatalog;
    final knownGameIds = <String>{};
    for (final entry in catalog) {
      knownGameIds.add(entry.gameId);
      final isOwnedByEntitlement =
          _isGameOwnedByEntitlement(entry, atUtc: nowUtc);
      final requiresExplicitInstall =
          _isDemoCatalogGameId(entry.gameId) || entry.requiresExplicitLicense;
      final existing = _contentStatesByGameId[entry.gameId];
      final localBundledAlwaysReady =
          _isLocalBundledAlwaysReadyGame(entry.gameId);

      final defaultInstalledVersion = localBundledAlwaysReady
          ? entry.targetContentVersion
          : (requiresExplicitInstall ? null : entry.targetContentVersion);
      final defaultRuntimeStatus = isOwnedByEntitlement
          ? (localBundledAlwaysReady
              ? ContentRuntimeStatus.ready
              : (requiresExplicitInstall
                  ? ContentRuntimeStatus.notInstalled
                  : ContentRuntimeStatus.ready))
          : ContentRuntimeStatus.notInstalled;
      if (existing == null) {
        _contentStatesByGameId[entry.gameId] = PurchasedContentState(
          gameId: entry.gameId,
          owned: isOwnedByEntitlement,
          installedVersion: defaultInstalledVersion,
          targetVersion: entry.targetContentVersion,
          updateRequired: false,
          updateOptional: false,
          runtimeStatus: defaultRuntimeStatus,
          lastError: null,
          updatedAtUtc: nowUtc,
        );
        continue;
      }

      _contentStatesByGameId[entry.gameId] = existing.copyWith(
        owned: isOwnedByEntitlement,
        targetVersion: entry.targetContentVersion,
        installedVersion: isOwnedByEntitlement
            ? (localBundledAlwaysReady
                ? entry.targetContentVersion
                : existing.installedVersion)
            : null,
        runtimeStatus: isOwnedByEntitlement
            ? (localBundledAlwaysReady
                ? ContentRuntimeStatus.ready
                : existing.runtimeStatus)
            : ContentRuntimeStatus.notInstalled,
        updateRequired: isOwnedByEntitlement
            ? (localBundledAlwaysReady ? false : existing.updateRequired)
            : false,
        updateOptional: isOwnedByEntitlement
            ? (localBundledAlwaysReady ? false : existing.updateOptional)
            : false,
        lastError: isOwnedByEntitlement
            ? (localBundledAlwaysReady ? null : existing.lastError)
            : null,
        updatedAtUtc: nowUtc,
      );
    }

    _contentStatesByGameId.removeWhere(
      (gameId, _) => !knownGameIds.contains(gameId),
    );
    _questReportedContentGameIds.removeWhere(
      (gameId) => !knownGameIds.contains(gameId),
    );
    _latestPackageProbeByGameId.removeWhere(
      (gameId, _) => !knownGameIds.contains(gameId),
    );
  }

  PurchasedContentState _contentStateForGame(String gameId) {
    final existing = _contentStatesByGameId[gameId];
    if (existing != null) {
      return existing;
    }

    final fallbackEntry =
        _effectiveGameCatalog.where((entry) => entry.gameId == gameId);
    if (fallbackEntry.isNotEmpty) {
      final entry = fallbackEntry.first;
      final isOwnedByEntitlement = _isGameOwnedByEntitlement(
        entry,
        atUtc: DateTime.now().toUtc(),
      );
      final requiresExplicitInstall =
          _isDemoCatalogGameId(entry.gameId) || entry.requiresExplicitLicense;
      final localBundledAlwaysReady = _isLocalBundledAlwaysReadyGame(gameId);
      return PurchasedContentState(
        gameId: gameId,
        owned: isOwnedByEntitlement,
        installedVersion: localBundledAlwaysReady
            ? entry.targetContentVersion
            : (requiresExplicitInstall ? null : entry.targetContentVersion),
        targetVersion: entry.targetContentVersion,
        updateRequired: false,
        updateOptional: false,
        runtimeStatus: isOwnedByEntitlement
            ? (localBundledAlwaysReady
                ? ContentRuntimeStatus.ready
                : (requiresExplicitInstall
                    ? ContentRuntimeStatus.notInstalled
                    : ContentRuntimeStatus.ready))
            : ContentRuntimeStatus.notInstalled,
        lastError: null,
        updatedAtUtc: DateTime.now().toUtc(),
      );
    }

    return PurchasedContentState(
      gameId: gameId,
      owned: false,
      installedVersion: null,
      targetVersion: '0.0.0',
      updateRequired: false,
      updateOptional: false,
      runtimeStatus: ContentRuntimeStatus.notInstalled,
      lastError: null,
      updatedAtUtc: DateTime.now().toUtc(),
    );
  }

  bool _isLaunchableContentState(PurchasedContentState state) {
    return ContentLaunchGate.isLaunchable(
      state: state,
      contentDeliveryEnabled: _contentDeliveryEnabled,
      hasQuestStatusSignal: _hasQuestReportedContentState(state.gameId),
    );
  }

  bool _hasQuestReportedContentState(String gameId) {
    if (!_contentDeliveryEnabled) {
      return true;
    }

    if (!_requiresQuestInstallState(gameId)) {
      return true;
    }

    return _questReportedContentGameIds.contains(gameId);
  }

  bool _isLocalBundledAlwaysReadyGame(String gameId) {
    final normalized = gameId.trim().toLowerCase();
    if (normalized.isEmpty) {
      return false;
    }

    return _localBundledAlwaysReadyGameIds.contains(normalized);
  }

  bool _requiresQuestInstallState(String gameId) {
    if (!_contentDeliveryEnabled) {
      return false;
    }

    if (_isDemoCatalogGameId(gameId)) {
      return true;
    }

    if (_isLocalBundledAlwaysReadyGame(gameId)) {
      return false;
    }

    for (final entry in _effectiveGameCatalog) {
      if (entry.gameId != gameId) {
        continue;
      }

      final hasPackageUri = entry.packageUri.trim().isNotEmpty;
      return entry.requiresExplicitLicense ||
          entry.availableForPurchase ||
          hasPackageUri;
    }

    return true;
  }

  String _buildLaunchReadinessHint(
    _GameCatalogEntry entry,
    PurchasedContentState state,
  ) {
    if (!_contentDeliveryEnabled) {
      return 'Selected game `${entry.title}` is not available for launch.';
    }

    if (!_hasQuestReportedContentState(state.gameId)) {
      return 'Waiting for headset install status sync. Tap Refresh in catalog first.';
    }

    if (!state.owned) {
      return entry.availableForPurchase
          ? 'Selected game `${entry.title}` is not owned yet. Add/install it from Store first.'
          : 'Selected game `${entry.title}` is not licensed for this account.';
    }

    if (state.runtimeStatus == ContentRuntimeStatus.syncingManifest) {
      return 'Selected game `${entry.title}` is syncing manifest. Wait for READY status.';
    }

    if (state.runtimeStatus == ContentRuntimeStatus.downloading) {
      return 'Selected game `${entry.title}` is downloading content. Wait for READY status.';
    }

    if (state.runtimeStatus == ContentRuntimeStatus.verifying) {
      return 'Selected game `${entry.title}` is verifying package integrity. Wait for READY status.';
    }

    if (state.runtimeStatus == ContentRuntimeStatus.activating) {
      return 'Selected game `${entry.title}` is activating content. Wait for READY status.';
    }

    if (state.runtimeStatus == ContentRuntimeStatus.rollingBack) {
      return 'Selected game `${entry.title}` is rolling back after verify failure. Wait for final status.';
    }

    if (state.runtimeStatus == ContentRuntimeStatus.installing) {
      return 'Selected game `${entry.title}` is currently installing. Wait for READY status.';
    }

    if (state.runtimeStatus == ContentRuntimeStatus.updateRequired ||
        state.updateRequired) {
      return 'Selected game `${entry.title}` requires update before launch.';
    }

    if (state.runtimeStatus == ContentRuntimeStatus.failed) {
      final lastError = state.lastError?.trim() ?? '';
      if (lastError.isNotEmpty) {
        return 'Selected game `${entry.title}` is in FAILED state ($lastError). Run install/update again.';
      }
      return 'Selected game `${entry.title}` is in FAILED state. Run install/update again.';
    }

    return 'Selected game `${entry.title}` needs install/update before opening.';
  }

  void _applyContentInstallStatusSignal(ContentInstallStatusSignal signal) {
    final state = signal.state;
    if (state.gameId.trim().isEmpty) {
      return;
    }

    var nextState = state;
    if (_isLocalBundledAlwaysReadyGame(state.gameId)) {
      var targetVersion = state.targetVersion.trim();
      if (targetVersion.isEmpty) {
        for (final entry in _effectiveGameCatalog) {
          if (entry.gameId == state.gameId) {
            targetVersion = entry.targetContentVersion.trim();
            break;
          }
        }
      }

      if (targetVersion.isEmpty) {
        targetVersion = '1.0.0';
      }

      nextState = state.copyWith(
        owned: true,
        targetVersion: targetVersion,
        installedVersion: targetVersion,
        runtimeStatus: ContentRuntimeStatus.ready,
        updateRequired: false,
        updateOptional: false,
        lastError: null,
        updatedAtUtc: DateTime.now().toUtc(),
      );
    }

    setState(() {
      _contentStatesByGameId[nextState.gameId] = nextState;
      _questReportedContentGameIds.add(nextState.gameId);
      _contentActionsInFlight.remove(nextState.gameId);
      if (_contentActionsInFlight.isEmpty) {
        _contentSyncInFlight = false;
      }
    });
  }

  void _applyPackageProbeResultSignal(PackageProbeResultSignal signal) {
    final gameId = signal.gameId.trim();
    if (gameId.isEmpty) {
      return;
    }

    setState(() {
      _latestPackageProbeByGameId[gameId] = signal;
      _contentActionsInFlight.remove(gameId);
    });
    unawaited(_persistPackageProbeResult(signal));

    final statusLabel =
        signal.statusCode > 0 ? '${signal.statusCode}' : signal.reasonCode;
    final summary = signal.success
        ? 'Package probe OK ($statusLabel)'
        : 'Package probe failed ($statusLabel)';
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text('$summary for $gameId'),
        duration: const Duration(seconds: 2),
        backgroundColor: signal.success ? null : Colors.orange.shade800,
      ),
    );
  }

  Future<void> _persistPackageProbeResult(
      PackageProbeResultSignal signal) async {
    final sessionId = _activeSessionId.trim();
    if (sessionId.isEmpty) {
      return;
    }

    try {
      await SessionJournalService.appendSessionEvent(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        eventType: 'PACKAGE_PROBE_RESULT',
        gameId: signal.gameId,
        source: 'vr_runtime',
        eventAtUtc: signal.probedAtUtc,
        details: <String, dynamic>{
          'success': signal.success,
          'statusCode': signal.statusCode,
          'contentLength': signal.contentLength,
          'method': signal.method,
          'eTag': signal.eTag,
          'contentType': signal.contentType,
          'reasonCode': signal.reasonCode,
          'probeOnly': signal.probeOnly,
          'packageUri': signal.packageUri,
        },
      );
    } catch (e) {
      debugPrint('[ControlScreen] Persist package probe result failed: $e');
    }
  }

  Future<void> _syncContentCatalog({bool silent = false}) async {
    if (!_contentDeliveryEnabled) {
      return;
    }

    if (!_isConnected || _contentSyncInFlight) {
      return;
    }

    setState(() {
      _contentSyncInFlight = true;
    });

    final success = await _sendCommand(
      ContentDeliveryCommandIds.syncCatalog,
      extraPayload: ContentDeliveryRequests.buildSyncCatalogRequest(
        actorId: _resolveActorTherapistId(),
        role:
            (EntitlementService.activeAccess?.role ?? EntitlementRole.therapist)
                .wireValue,
      ),
      showSuccessSnack: false,
    );

    if (mounted) {
      setState(() {
        _contentSyncInFlight = false;
      });
    }

    if (success && mounted && !silent) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Catalog sync requested'),
          duration: Duration(seconds: 1),
        ),
      );
    }
  }

  void _handlePotentialSessionDecisionGate(
    SessionStateUpdateSignal? sessionUpdate,
    RuntimeStatusUpdateSignal? runtimeUpdate,
    SessionWatchdogHeartbeatSignal? watchdogHeartbeat,
  ) {
    final handledByPersisted = _applyPersistedSessionDecisionGate();
    if (handledByPersisted) {
      return;
    }

    if (_serverAuthoritativeHandoffGate) {
      return;
    }

    final remoteSessionId = _resolveRemoteSessionId(
        sessionUpdate, runtimeUpdate, watchdogHeartbeat);
    if (remoteSessionId == null || remoteSessionId.isEmpty) {
      return;
    }

    final heartbeatState = SessionLifecycleState.tryParse(
      watchdogHeartbeat?.sessionState,
    );
    SessionLifecycleState? remoteState = sessionUpdate?.state ?? heartbeatState;
    if (remoteState == null &&
        _lastSessionStateUpdateSessionId == remoteSessionId) {
      remoteState = _sessionLifecycleState;
    }

    TherapistRuntimeStatus? remoteRuntime =
        runtimeUpdate?.status ?? watchdogHeartbeat?.runtimeStatus;
    if (remoteRuntime == null &&
        _lastRuntimeStatusSessionId == remoteSessionId) {
      remoteRuntime = _runtimeStatus;
    }

    final hasStrongNonTerminalState = remoteState != null &&
        SessionRecoveryPolicy.shouldGateBySessionState(remoteState);
    final hasStrongNonTerminalRuntime =
        _isRuntimeStatusNonTerminal(remoteRuntime);

    if (_wasSessionRecentlyEnded(remoteSessionId) &&
        !hasStrongNonTerminalState &&
        !hasStrongNonTerminalRuntime) {
      _clearDeferredHandoff(
        sessionId: remoteSessionId,
        source: 'runtime',
        reasonCode: 'SESSION_RECENTLY_TERMINAL',
      );
      if (_requiresSessionDecision && mounted) {
        setState(() {
          _requiresSessionDecision = false;
          _remoteSessionIdPendingDecision = null;
        });
      }
      return;
    }
    if (_wasSessionRecentlyEnded(remoteSessionId) &&
        (hasStrongNonTerminalState || hasStrongNonTerminalRuntime)) {
      _recentlyEndedSessionIds.remove(remoteSessionId);
    }

    final isRemoteTerminal = remoteState != null &&
        SessionRecoveryPolicy.isTerminalState(remoteState);
    if (isRemoteTerminal) {
      _markSessionAsRecentlyEnded(remoteSessionId);
      _clearDeferredHandoff(
        sessionId: remoteSessionId,
        source: 'runtime',
        reasonCode: 'REMOTE_STATE_TERMINAL',
      );
      if (_requiresSessionDecision && mounted) {
        setState(() {
          _requiresSessionDecision = false;
          _remoteSessionIdPendingDecision = null;
        });
      }
      return;
    }

    final needsDecision = SessionRecoveryPolicy.shouldRequireDecision(
      localSessionId: _activeSessionId,
      remoteSessionId: remoteSessionId,
      remoteState: remoteState,
      remoteRuntimeStatus: remoteRuntime,
    );

    if (needsDecision) {
      if (!_isSessionDecisionAllowedByContext()) {
        _markDeferredHandoff(
          sessionId: remoteSessionId,
          reasonCode: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
          source: 'runtime',
        );
        _logSessionDecision(
          source: 'runtime',
          decision: 'SUPPRESS',
          sessionId: remoteSessionId,
          reason: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
        );
        return;
      }

      final recoveryEvaluation = _evaluateRecoveryWindowState(
        remoteSessionNeedsDecision: true,
        remoteSessionId: remoteSessionId,
      );
      _clearDeferredHandoff(
        sessionId: remoteSessionId,
        source: 'runtime',
        reasonCode: 'REVIEW_DIALOG_OPENED',
      );

      if (mounted) {
        setState(() {
          _remoteSessionIdPendingDecision = remoteSessionId;
          _requiresSessionDecision = true;
        });
      } else {
        _remoteSessionIdPendingDecision = remoteSessionId;
        _requiresSessionDecision = true;
      }
      _logSessionDecision(
        source: 'runtime',
        decision: 'SHOW_DIALOG',
        sessionId: remoteSessionId,
        reason: _recoveryStateReasonCode(recoveryEvaluation.state),
      );
      _promptSessionDecisionIfNeeded();
      return;
    }

    final shouldUpdateActiveSession =
        remoteState == SessionLifecycleState.created &&
            remoteSessionId != _activeSessionId;
    final shouldClearDecisionState = _requiresSessionDecision;
    _clearDeferredHandoff(
      sessionId: remoteSessionId,
      source: 'runtime',
      reasonCode: 'RUNTIME_GATE_CLEARED',
    );

    if (!shouldUpdateActiveSession && !shouldClearDecisionState) {
      return;
    }

    setState(() {
      if (shouldUpdateActiveSession) {
        _activeSessionId = remoteSessionId;
      }
      if (shouldClearDecisionState) {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      }
    });
  }

  void _handleSessionTerminalStateFeedback({
    required SessionLifecycleState? previousState,
    required SessionStateUpdateSignal? sessionUpdate,
    required RuntimeStatusUpdateSignal? runtimeUpdate,
  }) {
    if (!mounted || sessionUpdate == null) {
      return;
    }

    final state = sessionUpdate.state;
    final isTerminal = SessionRecoveryPolicy.isTerminalState(state);
    final wasTerminal = previousState != null &&
        SessionRecoveryPolicy.isTerminalState(previousState);

    if (!isTerminal || wasTerminal) {
      return;
    }

    _clearReconnectGameRuntimeRecovery(reasonCode: 'SESSION_TERMINAL');

    final runtime = runtimeUpdate?.status;
    if (state == SessionLifecycleState.completed) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Game completed. You can adjust settings and start a new round.',
          ),
          duration: Duration(seconds: 2),
        ),
      );
    } else {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Session ended (${state.wireValue}${runtime == null ? '' : ', runtime=${runtime.wireValue}'}).',
          ),
          duration: const Duration(seconds: 2),
        ),
      );
    }
  }

  String? _resolveRemoteSessionId(
    SessionStateUpdateSignal? sessionUpdate,
    RuntimeStatusUpdateSignal? runtimeUpdate,
    SessionWatchdogHeartbeatSignal? watchdogHeartbeat,
  ) {
    final fromSessionState = sessionUpdate?.sessionId ?? '';
    if (fromSessionState.isNotEmpty) {
      return fromSessionState;
    }

    final fromRuntimeStatus = runtimeUpdate?.sessionId ?? '';
    if (fromRuntimeStatus.isNotEmpty) {
      return fromRuntimeStatus;
    }

    final fromWatchdogHeartbeat = watchdogHeartbeat?.sessionId ?? '';
    if (fromWatchdogHeartbeat.isNotEmpty) {
      return fromWatchdogHeartbeat;
    }

    return null;
  }

  String _resolveSessionIdForCriticalCommand(String command) {
    final activeSessionId = _activeSessionId.trim();
    final pendingDecisionSessionId = _requiresSessionDecision
        ? (_remoteSessionIdPendingDecision?.trim() ?? '')
        : '';
    final lastSessionSignalId = _lastSessionStateUpdateSessionId?.trim() ?? '';
    final lastRuntimeSignalId = _lastRuntimeStatusSessionId?.trim() ?? '';
    final persistedSessionId = _latestPersistedSession?.sessionId.trim() ?? '';

    if (command == CriticalCommandIds.endSession) {
      // Mobile-only arch: prefer activeSessionId (always mobile-*) so
      // END_SESSION side-effects land in the correct Firestore document.
      for (final candidate in <String>[
        activeSessionId,
        pendingDecisionSessionId,
        lastSessionSignalId,
        lastRuntimeSignalId,
      ]) {
        if (candidate.isNotEmpty) {
          return candidate;
        }
      }

      if (persistedSessionId.isNotEmpty) {
        return persistedSessionId;
      }
      return activeSessionId;
    }

    if (_gameScopedCriticalCommands.contains(command)) {
      for (final candidate in <String>[
        lastSessionSignalId,
        lastRuntimeSignalId,
        activeSessionId,
      ]) {
        if (candidate.isNotEmpty) {
          return candidate;
        }
      }
    }

    return activeSessionId;
  }

  int _resolveCriticalCommandMaxRetries() {
    final configured = _therapistSessionSettings.criticalCommandMaxRetries;
    if (configured < TherapistSessionSettings.minCriticalCommandMaxRetries) {
      return TherapistSessionSettings.minCriticalCommandMaxRetries;
    }
    if (configured > TherapistSessionSettings.maxCriticalCommandMaxRetries) {
      return TherapistSessionSettings.maxCriticalCommandMaxRetries;
    }
    return configured;
  }

  Duration _resolveCriticalCommandAckTimeout(String commandId) {
    var timeoutMs = _therapistSessionSettings.criticalCommandAckTimeoutMs;

    if ((commandId == CriticalCommandIds.startGame ||
            commandId == CriticalCommandIds.resumeGame) &&
        timeoutMs < 6000) {
      timeoutMs = 6000;
    }

    if (timeoutMs < TherapistSessionSettings.minCriticalCommandAckTimeoutMs) {
      timeoutMs = TherapistSessionSettings.minCriticalCommandAckTimeoutMs;
    } else if (timeoutMs >
        TherapistSessionSettings.maxCriticalCommandAckTimeoutMs) {
      timeoutMs = TherapistSessionSettings.maxCriticalCommandAckTimeoutMs;
    }

    return Duration(milliseconds: timeoutMs);
  }

  void _promptSessionDecisionIfNeeded() {
    if (!mounted || !_requiresSessionDecision || _isSessionDecisionDialogOpen) {
      return;
    }

    if (!_isSessionDecisionAllowedByContext()) {
      final deferredSessionId = _remoteSessionIdPendingDecision?.trim() ?? '';
      if (deferredSessionId.isNotEmpty) {
        _markDeferredHandoff(
          sessionId: deferredSessionId,
          reasonCode: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
          source: 'prompt',
        );
      }
      _logSessionDecision(
        source: 'prompt',
        decision: 'SKIP_DIALOG',
        sessionId: _remoteSessionIdPendingDecision,
        reason: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
      );
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
      return;
    }

    _logSessionDecision(
      source: 'prompt',
      decision: 'OPEN_DIALOG',
      sessionId: _remoteSessionIdPendingDecision,
      reason: 'PENDING_DECISION',
    );
    _isSessionDecisionDialogOpen = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      unawaited(_showSessionDecisionDialog());
    });
  }

  TherapySessionRecord? _resolveDecisionSessionRecord(String remoteSessionId) {
    final normalizedSessionId = remoteSessionId.trim();
    final persisted = _latestPersistedSession;
    if (persisted == null) {
      return null;
    }

    return persisted.sessionId.trim() == normalizedSessionId ? persisted : null;
  }

  String _resolveCatalogGameTitle(String gameId) {
    final normalizedGameId = gameId.trim();
    if (normalizedGameId.isEmpty) {
      return '';
    }

    for (final entry in _effectiveGameCatalog) {
      if (entry.gameId == normalizedGameId) {
        return entry.title;
      }
    }

    return normalizedGameId;
  }

  String _formatSessionStateLabel(SessionLifecycleState? state) {
    switch (state) {
      case SessionLifecycleState.created:
        return 'utworzona';
      case SessionLifecycleState.inProgress:
        return 'w trakcie';
      case SessionLifecycleState.paused:
        return 'wstrzymana';
      case SessionLifecycleState.interrupted:
        return 'przerwana';
      case SessionLifecycleState.completed:
        return 'zakończona';
      case SessionLifecycleState.abortedByTherapist:
        return 'porzucona przez terapeutę';
      case SessionLifecycleState.failedTechnical:
        return 'zakończona technicznie';
      case null:
        return 'nieznany';
    }
  }

  String _formatRelativeAge(Duration? age) {
    if (age == null) {
      return 'brak danych';
    }

    final normalizedAge = age.isNegative ? Duration.zero : age;
    if (normalizedAge < const Duration(minutes: 1)) {
      return 'mniej niż minutę temu';
    }
    if (normalizedAge < const Duration(hours: 1)) {
      return '${normalizedAge.inMinutes} min temu';
    }
    if (normalizedAge < const Duration(days: 1)) {
      return '${normalizedAge.inHours} godz. temu';
    }

    return '${normalizedAge.inDays} dni temu';
  }

  String _buildSessionDecisionDialogContent({
    required String remoteSessionId,
    required SessionRecoveryEvaluation recoveryEvaluation,
  }) {
    final persisted = _resolveDecisionSessionRecord(remoteSessionId);
    final latestGameId = persisted?.latestGameId.trim() ?? '';
    final lastGameTitle = _resolveCatalogGameTitle(latestGameId);
    final age = recoveryEvaluation.connectionLossAge ??
        (persisted?.updatedAtUtc != null
            ? DateTime.now()
                .toUtc()
                .difference(persisted!.updatedAtUtc!.toUtc())
            : null);
    final recoveryWindowMinutes =
        _therapistSessionSettings.sessionRecoveryWindowMinutes;
    final buffer = StringBuffer()
      ..writeln('Znaleziono niedomkniętą sesję dla tego dziecka.')
      ..writeln()
      ..writeln('Aby nie mieszać danych, wybierz jedną ścieżkę:')
      ..writeln(
          '• Kontynuuj starą sesję i zapisuj dalej do tego samego sessionId.')
      ..writeln('• Porzuć starą sesję, zachowaj jej historię i utwórz nową.');

    if (recoveryEvaluation.state ==
        SessionRecoveryWindowState
            .interruptedOverWindowNeedsTherapistDecision) {
      buffer
        ..writeln()
        ..writeln(
          'Okno odzyskiwania ($recoveryWindowMinutes min) zostało przekroczone, '
          'więc decyzja musi być jawna.',
        );
    }

    if (lastGameTitle.isNotEmpty) {
      buffer
        ..writeln()
        ..writeln('Ostatnia gra: $lastGameTitle');
    }

    if (persisted != null) {
      buffer
          .writeln('Stan sesji: ${_formatSessionStateLabel(persisted.state)}');
    }

    buffer.writeln('Ostatnia aktywność: ${_formatRelativeAge(age)}');
    return buffer.toString().trim();
  }

  Future<void> _showSessionDecisionDialog() async {
    if (!mounted) {
      _isSessionDecisionDialogOpen = false;
      return;
    }

    final remoteSessionId = _remoteSessionIdPendingDecision;
    if (remoteSessionId == null ||
        remoteSessionId.isEmpty ||
        !_requiresSessionDecision) {
      _isSessionDecisionDialogOpen = false;
      return;
    }

    if (!_isSessionDecisionAllowedByContext()) {
      _markDeferredHandoff(
        sessionId: remoteSessionId,
        reasonCode: 'CONTEXT_NOT_ENTRY_OR_RECONNECT',
        source: 'dialog',
      );
      _logSessionDecision(
        source: 'dialog',
        decision: 'SKIP_DIALOG',
        sessionId: remoteSessionId,
        reason: 'CONTEXT_CHANGED',
      );
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
      _isSessionDecisionDialogOpen = false;
      return;
    }

    final recoveryEvaluation = _evaluateRecoveryWindowState(
      remoteSessionNeedsDecision: true,
      remoteSessionId: remoteSessionId,
      interruptedAtUtc:
          _latestPersistedSession?.sessionId.trim() == remoteSessionId
              ? _latestPersistedSession?.interruptedAtUtc
              : null,
    );
    final action = await showDialog<_SessionGateAction>(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        return PopScope(
          canPop: false,
          child: AlertDialog(
            title: const Text('Niedomknięta sesja'),
            content: Text(
              _buildSessionDecisionDialogContent(
                remoteSessionId: remoteSessionId,
                recoveryEvaluation: recoveryEvaluation,
              ),
            ),
            actions: [
              TextButton(
                onPressed: () =>
                    Navigator.of(context).pop(_SessionGateAction.resume),
                child: const Text('Kontynuuj starą'),
              ),
              ElevatedButton(
                onPressed: () =>
                    Navigator.of(context).pop(_SessionGateAction.startNew),
                child: const Text('Porzuć i zacznij nową'),
              ),
            ],
          ),
        );
      },
    );

    if (!mounted) {
      _isSessionDecisionDialogOpen = false;
      return;
    }

    switch (action) {
      case _SessionGateAction.resume:
        await _handleResumeDecision(remoteSessionId);
        break;
      case _SessionGateAction.startNew:
        await _handleStartNewDecision(remoteSessionId);
        break;
      case null:
        _logSessionDecision(
          source: 'dialog',
          decision: 'DISMISS_WITHOUT_DECISION',
          sessionId: remoteSessionId,
          reason: 'DIALOG_DISMISSED_UNEXPECTEDLY',
        );
        break;
    }

    _isSessionDecisionDialogOpen = false;
    if (_requiresSessionDecision) {
      _promptSessionDecisionIfNeeded();
    }
  }

  Future<void> _handleResumeDecision(String remoteSessionId) async {
    _logSessionDecision(
      source: 'dialog',
      decision: 'RESUME_REMOTE',
      sessionId: remoteSessionId,
      reason: 'THERAPIST_CONTINUE_UNFINISHED',
    );
    final resolvedGameId = _resolveRemoteGameIdForResume();
    final recoveryEvaluation = _evaluateRecoveryWindowState(
      remoteSessionNeedsDecision: true,
      remoteSessionId: remoteSessionId,
      interruptedAtUtc:
          _latestPersistedSession?.sessionId.trim() == remoteSessionId
              ? _latestPersistedSession?.interruptedAtUtc
              : null,
    );

    setState(() {
      // Mobile-only arch guard: only adopt mobile-* IDs as active session.
      // If remoteSessionId is a UUID (old Firestore data), keep current mobile-*.
      if (remoteSessionId.startsWith('mobile-')) {
        _activeSessionId = remoteSessionId;
      }
      _requiresSessionDecision = false;
      _remoteSessionIdPendingDecision = null;
      _lastConnectionLostAtUtc = null;
      _sessionAttachReady = false;
      _workflowStep = _WorkflowStep.gameSetup;
      _isVideoPreviewExpanded = true;
      if (resolvedGameId != null) {
        _selectedGameId = resolvedGameId;
      }
    });
    unawaited(
      _persistWorkflowCheckpoint(
        workflowStep: _WorkflowStep.gameSetup,
        reasonCode: 'SCREEN_GAME_SETUP',
        gameIdOverride: resolvedGameId,
      ),
    );
    _clearDeferredHandoff(
      sessionId: remoteSessionId,
      source: 'dialog',
      reasonCode: 'THERAPIST_CONTINUE_UNFINISHED',
    );
    _persistResumeDecisionOutcome(
      sessionId: remoteSessionId,
      resumedGameId: resolvedGameId,
      recoveryState: recoveryEvaluation.state,
    );
    await _ensureSessionAttached(
      reasonCode: 'HANDOFF_RESUME',
      force: true,
    );
    if (mounted && _sessionAttachReady) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Session continued. Use Start/Resume/Pause controls to proceed.',
          ),
        ),
      );
    }
  }

  String? _resolveRemoteGameIdForResume() {
    final remoteGameId = _remoteActiveGameId?.trim() ?? '';
    if (remoteGameId.isEmpty) {
      return null;
    }

    for (final entry in _effectiveGameCatalog) {
      if (entry.gameId == remoteGameId) {
        return remoteGameId;
      }
    }

    return null;
  }

  String _resolveDecisionSessionGameId(
    String sessionId, {
    String fallbackGameId = '',
  }) {
    final persistedGameId =
        _resolveDecisionSessionRecord(sessionId)?.latestGameId.trim() ?? '';
    if (persistedGameId.isNotEmpty) {
      return persistedGameId;
    }

    final normalizedFallbackGameId = fallbackGameId.trim();
    if (normalizedFallbackGameId.isNotEmpty) {
      return normalizedFallbackGameId;
    }

    return _selectedGameId.trim();
  }

  void _persistResumeDecisionOutcome({
    required String sessionId,
    String? resumedGameId,
    required SessionRecoveryWindowState recoveryState,
  }) {
    final normalizedSessionId = sessionId.trim();
    if (normalizedSessionId.isEmpty) {
      return;
    }

    debugPrint(
      '[ControlScreen] Resume decision kept local-only: '
      'session=$normalizedSessionId game=${_resolveDecisionSessionGameId(normalizedSessionId, fallbackGameId: resumedGameId ?? '')} '
      'recoveryState=${recoveryState.name}',
    );
  }

  Future<void> _bootstrapReplacementSession({
    required String newSessionId,
    required String previousSessionId,
  }) async {
    final normalizedNewSessionId = newSessionId.trim();
    final normalizedPreviousSessionId = previousSessionId.trim();
    if (normalizedNewSessionId.isEmpty || normalizedPreviousSessionId.isEmpty) {
      return;
    }

    final therapistId = _resolveActorTherapistId();
    final previousGameId =
        _resolveDecisionSessionGameId(normalizedPreviousSessionId);
    final metadata = <String, dynamic>{
      'origin': 'mobile_decision_gate',
      'previousSessionId': normalizedPreviousSessionId,
      'previousGameId': previousGameId,
      'decision': 'START_NEW',
    };
    try {
      await SessionJournalService.upsertSessionState(
        sessionId: normalizedNewSessionId,
        studentId: widget.student.id,
        therapistId: therapistId,
        state: SessionLifecycleState.created,
        latestGameId: '',
        reasonCode: 'THERAPIST_STARTED_REPLACEMENT_SESSION',
        metadata: metadata,
      );

      _setLatestPersistedSessionSnapshotLocally(
        _buildLocalPersistedSessionSnapshot(
          sessionId: normalizedNewSessionId,
          state: SessionLifecycleState.created,
          latestGameId: '',
          reasonCode: 'THERAPIST_STARTED_REPLACEMENT_SESSION',
          metadata: metadata,
        ),
      );
    } catch (e) {
      debugPrint(
        '[ControlScreen] Bootstrap replacement session failed: '
        'session=$normalizedNewSessionId previous=$normalizedPreviousSessionId '
        'error=$e',
      );
    }
  }

  Future<void> _handleStartNewDecision(String remoteSessionId) async {
    _logSessionDecision(
      source: 'dialog',
      decision: 'START_NEW_SELECTED',
      sessionId: remoteSessionId,
      reason: 'THERAPIST_START_NEW_REQUESTED',
    );
    final confirmed = await showDialog<bool>(
          context: context,
          barrierDismissible: false,
          builder: (context) {
            return PopScope(
              canPop: false,
              child: AlertDialog(
                title: const Text('Porzucić starą sesję?'),
                content: const Text(
                  'Stara sesja zostanie oznaczona jako porzucona, jej dane '
                  'zostaną zachowane, a my utworzymy nową sesję. Kontynuować?',
                ),
                actions: [
                  TextButton(
                    onPressed: () => Navigator.of(context).pop(false),
                    child: const Text('Anuluj'),
                  ),
                  ElevatedButton(
                    onPressed: () => Navigator.of(context).pop(true),
                    child: const Text('Porzuć i zacznij nową'),
                  ),
                ],
              ),
            );
          },
        ) ??
        false;

    if (!confirmed || !mounted) {
      _logSessionDecision(
        source: 'dialog',
        decision: 'START_NEW_CANCELLED',
        sessionId: remoteSessionId,
        reason: 'THERAPIST_CANCELLED_CONFIRMATION',
      );
      return;
    }

    final replacementSessionId = _buildLocalSessionId();
    try {
      await _connection.sendCriticalCommand(
        commandId: CriticalCommandIds.endSession,
        sessionId: remoteSessionId,
        payload: _buildCriticalPayload(
          CriticalCommandIds.endSession,
          sessionId: remoteSessionId,
          extraPayload: <String, dynamic>{
            'reasonCode': 'THERAPIST_ABANDONED_UNFINISHED_SESSION',
            'reason': 'TherapistAbandonedUnfinishedSession',
            'replacementSessionId': replacementSessionId,
          },
        ),
        expiresAtUtc: DateTime.now().toUtc().add(const Duration(seconds: 30)),
        ackTimeout:
            _resolveCriticalCommandAckTimeout(CriticalCommandIds.endSession),
        maxRetries: _resolveCriticalCommandMaxRetries(),
      );
      _markSessionAsRecentlyEnded(remoteSessionId);
      await _persistStartNewDecisionOutcome(
        remoteSessionId: remoteSessionId,
        replacementSessionId: replacementSessionId,
      );
      await _bootstrapReplacementSession(
        newSessionId: replacementSessionId,
        previousSessionId: remoteSessionId,
      );

      if (!mounted) {
        return;
      }

      setState(() {
        _activeSessionId = replacementSessionId;
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
        _lastConnectionLostAtUtc = null;
        _sessionAttachReady = false;
        _workflowStep = _WorkflowStep.gameCatalog;
        _isVideoPreviewExpanded = false;
      });
      unawaited(
        _persistWorkflowCheckpoint(
          workflowStep: _WorkflowStep.gameCatalog,
          reasonCode: 'SCREEN_GAME_CATALOG',
        ),
      );
      _clearReconnectGameRuntimeRecovery(reasonCode: 'START_NEW_SESSION');
      _clearDeferredHandoff(
        sessionId: remoteSessionId,
        source: 'dialog',
        reasonCode: 'THERAPIST_ABANDONED_UNFINISHED_SESSION',
      );
      await _ensureSessionAttached(
        reasonCode: 'HANDOFF_START_NEW',
        force: true,
        sessionIdOverride: replacementSessionId,
      );
      if (!mounted || !_sessionAttachReady) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Stara sesja została zachowana jako porzucona. Możesz uruchomić nową.',
          ),
        ),
      );
      _logSessionDecision(
        source: 'dialog',
        decision: 'START_NEW_COMPLETED',
        sessionId: remoteSessionId,
        reason: 'ABANDON_OLD_AND_ATTACH_OK',
      );
    } catch (e) {
      if (!mounted) {
        return;
      }

      final errorSummary = OpsErrorCatalog.buildOperatorSummary(
        error: e,
      );
      _logSessionDecision(
        source: 'dialog',
        decision: 'START_NEW_FAILED',
        sessionId: remoteSessionId,
        reason: 'END_SESSION_FAILED',
      );
      _enqueueIncidentAlert(
        title: 'Could not end previous session',
        message: 'Could not end previous session: $errorSummary',
        reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ??
            'END_SESSION_STOP_FAILED',
        severity: OperatorIncidentSeverity.error,
      );
    }
  }

  bool _enforceSessionClosureLockForGameCommand({
    required String command,
    required String commandSessionId,
  }) {
    if (command != CriticalCommandIds.startGame &&
        command != CriticalCommandIds.resumeGame) {
      return true;
    }

    final persisted = _latestPersistedSession;
    if (persisted == null) {
      return true;
    }

    final persistedSessionId = persisted.sessionId.trim();
    final requiresClosure =
        SessionRecoveryPolicy.shouldRequireExplicitClosureForNewSession(
      targetSessionId: commandSessionId,
      persistedSessionId: persistedSessionId,
      persistedState: persisted.state,
      persistedRequiresHandoffDecision: persisted.requiresHandoffDecision,
      persistedSessionRecentlyEnded:
          _wasSessionRecentlyEnded(persistedSessionId),
    );
    if (!requiresClosure) {
      return true;
    }

    _logSessionDecision(
      source: 'closure_lock',
      decision: 'BLOCK_COMMAND',
      commandId: command,
      sessionId: persistedSessionId,
      reason: 'SESSION_CLOSURE_REQUIRED',
    );

    final canOpenDecisionDialog = _isSessionDecisionAllowedByContext();
    if (canOpenDecisionDialog) {
      if (mounted) {
        setState(() {
          _remoteSessionIdPendingDecision = persistedSessionId;
          _requiresSessionDecision = true;
        });
      } else {
        _remoteSessionIdPendingDecision = persistedSessionId;
        _requiresSessionDecision = true;
      }
      _promptSessionDecisionIfNeeded();
    } else {
      _markDeferredHandoff(
        sessionId: persistedSessionId,
        reasonCode: 'SESSION_CLOSURE_REQUIRED',
        source: 'closure_lock',
      );
    }

    if (mounted) {
      final shortSessionId = persistedSessionId.length > 16
          ? '${persistedSessionId.substring(0, 8)}...${persistedSessionId.substring(persistedSessionId.length - 4)}'
          : persistedSessionId;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            'Finish unfinished session [$shortSessionId] before starting a new one.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
    }

    return false;
  }

  Future<bool> _sendCommand(
    String command, {
    Map<String, dynamic>? extraPayload,
    bool showSuccessSnack = true,
  }) async {
    final isCriticalCommand = CriticalCommandIds.isCritical(command);
    if (isCriticalCommand) {
      _lastCriticalFailureReasonByCommand.remove(command);
    }

    if (_gameScopedCriticalCommands.contains(command) &&
        _selectedGameId.trim().isEmpty) {
      if (!mounted) {
        return false;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Select a game before sending this command.'),
          backgroundColor: Colors.orange,
        ),
      );
      return false;
    }

    if (_requiresSessionDecision && command != CriticalCommandIds.endSession) {
      _logSessionDecision(
        source: 'command',
        decision: 'BLOCK_COMMAND',
        commandId: command,
        sessionId: _remoteSessionIdPendingDecision,
        reason: 'PENDING_HANDOFF_DECISION',
      );
      if (!mounted) {
        return false;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Choose Resume or Start New before sending commands.'),
          backgroundColor: Colors.orange,
        ),
      );
      _promptSessionDecisionIfNeeded();
      return false;
    }

    if (CriticalCommandIds.isCritical(command) &&
        command != CriticalCommandIds.sessionAttach &&
        command != CriticalCommandIds.endSession &&
        !_sessionAttachReady) {
      _logAttachDecision(
        decision: 'REQUIRE_ATTACH_PRECONDITION',
        reasonCode: 'COMMAND_PRECONDITION',
        commandId: command,
      );
      await _ensureSessionAttached(
        reasonCode: 'COMMAND_PRECONDITION',
        force: true,
      );
      if (!_sessionAttachReady) {
        if (!mounted) {
          return false;
        }
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Waiting for session sync. Try again in a moment.'),
            backgroundColor: Colors.orange,
          ),
        );
        return false;
      }
    } else if (command == CriticalCommandIds.endSession &&
        !_sessionAttachReady) {
      _logAttachDecision(
        decision: 'BYPASS_ATTACH_PRECONDITION',
        reasonCode: 'END_SESSION_OVERRIDE',
        commandId: command,
      );
    } else if (CriticalCommandIds.isCritical(command) &&
        command != CriticalCommandIds.sessionAttach) {
      _logAttachDecision(
        decision: 'PRECONDITION_ALREADY_SATISFIED',
        reasonCode: 'COMMAND_PRECONDITION',
        commandId: command,
      );
    }

    if ((command == CriticalCommandIds.startGame ||
            command == CriticalCommandIds.resumeGame) &&
        _reconnectGameRuntimeRecoveryPending) {
      final recovered = await _ensurePreparedGameRuntimeAfterReconnect(
        trigger: command,
      );
      if (!recovered) {
        if (!mounted) {
          return false;
        }

        final recoveryLabel = _resolveCatalogGameTitle(
          _resolveReconnectGameRuntimeGameId(),
        );
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              recoveryLabel.isEmpty
                  ? 'Headset is still reloading the game scene. Try again in a moment.'
                  : 'Headset is still reloading $recoveryLabel. Try again in a moment.',
            ),
            backgroundColor: Colors.orange,
          ),
        );
        return false;
      }
    }

    final commandSessionId = CriticalCommandIds.isCritical(command)
        ? _resolveSessionIdForCriticalCommand(command)
        : _activeSessionId;
    if (CriticalCommandIds.isCritical(command) &&
        commandSessionId.trim().isEmpty) {
      if (!mounted) {
        return false;
      }

      _enqueueIncidentAlert(
        title: 'Critical command blocked',
        message: 'Cannot resolve active session id for this command.',
        reasonCode: 'SESSION_ID_REQUIRED',
        severity: OperatorIncidentSeverity.error,
      );
      return false;
    }

    if (!_enforceSessionClosureLockForGameCommand(
      command: command,
      commandSessionId: commandSessionId,
    )) {
      return false;
    }

    final resolvedPayload = CriticalCommandIds.isCritical(command)
        ? _buildCriticalPayload(
            command,
            sessionId: commandSessionId,
            extraPayload: extraPayload,
          )
        : extraPayload;

    try {
      if (CriticalCommandIds.isCritical(command)) {
        await _connection.sendCriticalCommand(
          commandId: command,
          sessionId: commandSessionId,
          payload: resolvedPayload,
          expiresAtUtc: DateTime.now().toUtc().add(const Duration(seconds: 30)),
          ackTimeout: _resolveCriticalCommandAckTimeout(command),
          maxRetries: _resolveCriticalCommandMaxRetries(),
        );
      } else {
        await _connection.sendCommand(command, extraPayload);
      }

      if (mounted) {
        setState(() {
          final stopReason = resolvedPayload == null
              ? null
              : resolvedPayload['reason'] as String?;
          if (command == CriticalCommandIds.startGame ||
              command == CriticalCommandIds.resumeGame) {
            _optimisticRuntimeActive = true;
            _optimisticRuntimePaused = false;
            _optimisticPreparedGameId = _selectedGameId;
          } else if (command == CriticalCommandIds.pauseGame) {
            _optimisticRuntimeActive = true;
            _optimisticRuntimePaused = true;
          } else if (command == CriticalCommandIds.stopGame ||
              command == CriticalCommandIds.endSession) {
            _optimisticRuntimeActive = false;
            _optimisticRuntimePaused = false;
            _remoteActiveGameId = null;
            _lastDevicePresenceSignal = _clearActiveGameFromPresenceSignal(
              _lastDevicePresenceSignal,
            );
            if (command == CriticalCommandIds.stopGame &&
                _shouldKeepPreparedGameAfterStopReason(stopReason)) {
              _optimisticPreparedGameId = _selectedGameId;
            } else {
              _optimisticPreparedGameId = null;
            }
            if (command == CriticalCommandIds.stopGame &&
                _shouldReturnToCatalogAfterStopReason(stopReason)) {
              _workflowStep = _WorkflowStep.gameCatalog;
              _isVideoPreviewExpanded = false;
            }
          }
        });
      }

      if (command == CriticalCommandIds.stopGame ||
          command == CriticalCommandIds.endSession) {
        _clearReconnectGameRuntimeRecovery(reasonCode: 'COMMAND_TERMINAL');
      }

      if (command == CriticalCommandIds.endSession) {
        await _persistCommandSideEffects(
          command,
          sessionIdOverride: commandSessionId,
          extraPayload: resolvedPayload,
        );
      } else if (command == CriticalCommandIds.startGame ||
          command == CriticalCommandIds.pauseGame ||
          command == CriticalCommandIds.resumeGame ||
          command == CriticalCommandIds.stopGame) {
        unawaited(
          _persistCommandSideEffects(
            command,
            sessionIdOverride: commandSessionId,
            extraPayload: resolvedPayload,
          ),
        );
      }

      if (command == CriticalCommandIds.startGame) {
        _cacheAppliedGameConfigFromPayload(
          sessionId: commandSessionId,
          payload: resolvedPayload,
        );
      }

      if (!mounted || !showSuccessSnack) {
        if (isCriticalCommand) {
          _lastCriticalFailureReasonByCommand.remove(command);
        }
        return true;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Sent: $command'),
          duration: const Duration(seconds: 1),
        ),
      );
      if (isCriticalCommand) {
        _lastCriticalFailureReasonByCommand.remove(command);
      }
      return true;
    } catch (e) {
      if (!mounted) {
        return false;
      }

      final isCritical = isCriticalCommand;
      final failureReasonCode = OpsErrorCatalog.tryExtractReasonCode(e) ??
          (isCritical ? 'UNSPECIFIED' : 'UNKNOWN');
      if (isCritical) {
        _lastCriticalFailureReasonByCommand[command] = failureReasonCode;
      }
      final errorSummary = OpsErrorCatalog.buildOperatorSummary(
        error: e,
      );
      final message = isCritical
          ? 'Failed: $command - $errorSummary'
          : 'Failed: $command ($e)';

      _enqueueIncidentAlert(
        title: 'Command failed: $command',
        message: message,
        reasonCode: failureReasonCode,
        severity: OperatorIncidentSeverity.error,
      );
      return false;
    }
  }

  Map<String, dynamic> _buildCriticalPayload(
    String command, {
    required String sessionId,
    Map<String, dynamic>? extraPayload,
  }) {
    final therapistId = _resolveActorTherapistId();
    final ownerKey = _resolveOwnerKey(therapistIdOverride: therapistId);
    final sessionKey = _resolveSessionKey(
      sessionId,
      therapistIdOverride: therapistId,
    );
    final payload = <String, dynamic>{
      'sessionId': sessionId,
      'studentId': widget.student.id,
      'patientId': widget.student.id,
      'therapistId': therapistId,
      'ownerKey': ownerKey,
      'sessionKey': sessionKey,
    };

    if (_gameScopedCriticalCommands.contains(command)) {
      payload['gameId'] = _selectedGameId;
      payload['resumeFromSaved'] = false;
    }

    if (command == CriticalCommandIds.startGame) {
      final configSnapshot = _resolveStartGameConfigSnapshot(
        sessionId: sessionId,
      );
      if (configSnapshot != null) {
        payload['gameConfigType'] = configSnapshot.gameConfigType;
        payload['gameConfigVersion'] = configSnapshot.gameConfigVersion;
        payload['gameConfigJson'] = configSnapshot.gameConfigJson;
      }
    }

    if (command == CriticalCommandIds.stopGame) {
      payload['reason'] = 'UserExit';
    } else if (command == CriticalCommandIds.endSession) {
      payload['reason'] = 'TherapistEndedSession';
    }

    if (extraPayload != null && extraPayload.isNotEmpty) {
      payload.addAll(extraPayload);
    }

    return payload;
  }

  _AppliedGameConfigSnapshot? _resolveStartGameConfigSnapshot({
    required String sessionId,
  }) {
    final normalizedSessionId = sessionId.trim();
    if (normalizedSessionId.isEmpty) {
      return _buildCurrentStartGameConfigSnapshot();
    }

    final cacheKey = _buildSessionGameCacheKey(
      sessionId: normalizedSessionId,
      gameId: _selectedGameId,
    );
    final cached = _appliedConfigBySessionGame[cacheKey];
    if (cached != null) {
      return cached;
    }

    return _buildCurrentStartGameConfigSnapshot();
  }

  _AppliedGameConfigSnapshot? _buildCurrentStartGameConfigSnapshot() {
    final schema = _selectedGameEntry.mobileControlSchema;
    if (schema != null) {
      final schemaGameConfig = _buildSchemaDrivenGameConfigPayload(
        schema,
        emitForUpdateConfig: false,
      );
      return _AppliedGameConfigSnapshot(
        gameConfigType: _resolveSchemaGameConfigType(schema),
        gameConfigVersion: _resolveSchemaGameConfigVersion(schema),
        gameConfigJson: jsonEncode(schemaGameConfig),
      );
    }

    if (_isDemoCubeGameSelected) {
      return _AppliedGameConfigSnapshot(
        gameConfigType: 'demo_cube_config_v1',
        gameConfigVersion: 1,
        gameConfigJson: jsonEncode(<String, dynamic>{
          'cubeCount': _demoCubeCount,
          'cubeSpeed': double.parse(_demoCubeSpeed.toStringAsFixed(2)),
          'levelMode': _demoLevelMode,
          'version': 1,
        }),
      );
    }

    if (_isPulseTargetGameSelected) {
      final adaptiveDifficultySensitivity = double.parse(
        _therapistSessionSettings.adaptiveDifficultySensitivity
            .toStringAsFixed(2),
      );
      return _AppliedGameConfigSnapshot(
        gameConfigType: 'pulse_targets_config_v1',
        gameConfigVersion: 1,
        gameConfigJson: jsonEncode(<String, dynamic>{
          'targetCount': _pulseTargetCount,
          'targetSpeed': double.parse(_pulseTargetSpeed.toStringAsFixed(2)),
          'targetScale': double.parse(_pulseTargetScale.toStringAsFixed(2)),
          'adaptiveDifficultyEnabled':
              _therapistSessionSettings.adaptiveDifficultyEnabled,
          'adaptiveDifficultySensitivity': adaptiveDifficultySensitivity,
          'adaptiveDifficultyLevel': _resolveAdaptiveDifficultyLevel(),
          'labelPipelineEnabled':
              _therapistSessionSettings.labelPipelineEnabled,
          'version': 1,
        }),
      );
    }

    return null;
  }

  Map<String, dynamic> _buildStartGameTimelineDetails(
    Map<String, dynamic>? payload,
  ) {
    final resolvedPayload = payload == null
        ? <String, dynamic>{}
        : Map<String, dynamic>.from(payload);
    final gameConfigType =
        (resolvedPayload['gameConfigType'] as String? ?? '').trim();
    final gameConfigVersion =
        _asTimelineInt(resolvedPayload['gameConfigVersion']);
    final gameConfigJson =
        (resolvedPayload['gameConfigJson'] as String? ?? '').trim();

    return <String, dynamic>{
      'reasonCode': 'START_GAME_SENT',
      if (gameConfigType.isNotEmpty) 'gameConfigType': gameConfigType,
      if (gameConfigVersion > 0) 'gameConfigVersion': gameConfigVersion,
      if (gameConfigJson.isNotEmpty) 'gameConfigJson': gameConfigJson,
      if (gameConfigJson.isNotEmpty)
        'gameConfigSummary': _summarizeGameConfigJson(gameConfigJson),
    };
  }

  String _summarizeGameConfigJson(String rawJson) {
    final normalized = rawJson.trim();
    if (normalized.isEmpty) {
      return '';
    }

    try {
      final decoded = jsonDecode(normalized);
      if (decoded is! Map) {
        return _truncateTimelineConfigSummary(normalized);
      }

      final fragments = <String>[];
      for (final entry in decoded.entries) {
        final key = entry.key.toString().trim();
        if (key.isEmpty || key == 'version') {
          continue;
        }

        final valueText = _formatConfigSummaryValue(entry.value);
        if (valueText.isEmpty) {
          continue;
        }

        fragments.add('$key=$valueText');
        if (fragments.length >= 4) {
          break;
        }
      }

      if (fragments.isEmpty) {
        return _truncateTimelineConfigSummary(normalized);
      }

      return fragments.join(', ');
    } catch (_) {
      return _truncateTimelineConfigSummary(normalized);
    }
  }

  String _formatConfigSummaryValue(dynamic value) {
    if (value == null) {
      return '';
    }
    if (value is bool) {
      return value ? 'true' : 'false';
    }
    if (value is num) {
      return value.toString();
    }
    if (value is String) {
      return value.trim();
    }
    if (value is List) {
      return '[${value.length}]';
    }
    if (value is Map) {
      return '{${value.length}}';
    }
    return value.toString().trim();
  }

  String _truncateTimelineConfigSummary(String value) {
    const maxLength = 96;
    if (value.length <= maxLength) {
      return value;
    }
    return '${value.substring(0, maxLength - 1)}...';
  }

  void _cacheAppliedGameConfigFromPayload({
    required String sessionId,
    Map<String, dynamic>? payload,
  }) {
    if (payload == null) {
      return;
    }

    final normalizedSessionId = sessionId.trim();
    final gameId = (payload['gameId'] as String? ?? _selectedGameId).trim();
    final gameConfigType = (payload['gameConfigType'] as String? ?? '').trim();
    final gameConfigJson = (payload['gameConfigJson'] as String? ?? '').trim();
    final gameConfigVersionRaw = payload['gameConfigVersion'];
    final gameConfigVersion = gameConfigVersionRaw is int
        ? gameConfigVersionRaw
        : (gameConfigVersionRaw is num ? gameConfigVersionRaw.toInt() : 0);
    if (normalizedSessionId.isEmpty ||
        gameId.isEmpty ||
        gameConfigType.isEmpty ||
        gameConfigJson.isEmpty ||
        gameConfigVersion <= 0) {
      return;
    }

    final cacheKey = _buildSessionGameCacheKey(
      sessionId: normalizedSessionId,
      gameId: gameId,
    );
    _appliedConfigBySessionGame[cacheKey] = _AppliedGameConfigSnapshot(
      gameConfigType: gameConfigType,
      gameConfigVersion: gameConfigVersion,
      gameConfigJson: gameConfigJson,
    );
  }

  String _buildSessionGameCacheKey({
    required String sessionId,
    required String gameId,
  }) {
    return '${sessionId.trim()}|${gameId.trim()}';
  }

  Future<void> _handleSchemaButtonControl(
    MobileControlDefinition control,
  ) async {
    final normalizedCommand = control.buttonCommandId.trim().toUpperCase();
    if (normalizedCommand == _updateConfigCommandId) {
      await _sendUpdateConfigFromSchema();
      return;
    }

    if (!mounted) {
      return;
    }

    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          'Unsupported schema command: ${control.buttonCommandId.trim()}',
        ),
        backgroundColor: Colors.orange.shade700,
      ),
    );
  }

  Future<bool> _sendUpdateConfigFromSchema() async {
    final schema = _selectedGameEntry.mobileControlSchema;
    if (schema == null) {
      return false;
    }

    if (!_isControlLinkReadyForCommands) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              'Cannot send UPDATE_CONFIG. $_controlLinkBlockedHint',
            ),
            backgroundColor: Colors.orange,
          ),
        );
      }
      return false;
    }

    if (!_isSelectedGameSceneReadyForControls) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              'Cannot send UPDATE_CONFIG yet. $_selectedGameSceneLoadingHint',
            ),
            backgroundColor: Colors.orange,
          ),
        );
      }
      return false;
    }

    final sessionId = _activeSessionId;
    final therapistId = _resolveActorTherapistId();
    final ownerKey = _resolveOwnerKey(therapistIdOverride: therapistId);
    final sessionKey = _resolveSessionKey(
      sessionId,
      therapistIdOverride: therapistId,
    );
    final gameConfig = _buildSchemaDrivenGameConfigPayload(
      schema,
      emitForUpdateConfig: true,
    );

    final payload = <String, dynamic>{
      'sessionId': sessionId,
      'studentId': widget.student.id,
      'patientId': widget.student.id,
      'therapistId': therapistId,
      'ownerKey': ownerKey,
      'sessionKey': sessionKey,
      'gameId': _selectedGameId,
      'gameConfigType': _resolveSchemaGameConfigType(schema),
      'gameConfigVersion': _resolveSchemaGameConfigVersion(schema),
      'gameConfigJson': jsonEncode(gameConfig),
    };

    try {
      await _connection.sendCommand(_updateConfigCommandId, payload);
      _cacheAppliedGameConfigFromPayload(
        sessionId: sessionId,
        payload: payload,
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Configuration update sent.'),
            duration: Duration(seconds: 1),
          ),
        );
      }
      return true;
    } catch (e) {
      if (mounted) {
        _enqueueIncidentAlert(
          title: 'UPDATE_CONFIG failed',
          message: 'Failed to send UPDATE_CONFIG: $e',
          reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ??
              'UPDATE_CONFIG_SEND_FAILED',
          severity: OperatorIncidentSeverity.error,
        );
      }
      return false;
    }
  }

  int _resolveAdaptiveDifficultyLevel() {
    final normalized =
        _therapistSessionSettings.adaptiveDifficultySensitivity.clamp(0.0, 1.0);
    final scaled = 1 + (normalized * 4).round();
    if (scaled < 1) {
      return 1;
    }
    if (scaled > 5) {
      return 5;
    }
    return scaled;
  }

  Future<void> _runPrimaryAction(Future<void> Function() action) async {
    if (_isPrimaryActionInFlight) {
      return;
    }

    setState(() {
      _isPrimaryActionInFlight = true;
    });

    try {
      await action();
    } finally {
      if (mounted) {
        setState(() {
          _isPrimaryActionInFlight = false;
        });
      }
    }
  }

  GuidedSessionPlanStep? _resolveDefaultGuidedPlanStep() {
    final steps = _therapistSessionSettings.guidedSessionPlanSteps;
    if (steps.isEmpty) {
      return null;
    }

    final knownGameIds = _effectiveGameCatalog
        .map((entry) => entry.gameId)
        .where((gameId) => gameId.trim().isNotEmpty)
        .toSet();
    for (final step in steps) {
      final gameId = step.gameId.trim();
      if (gameId.isNotEmpty && knownGameIds.contains(gameId)) {
        return step;
      }
    }

    return null;
  }

  void _applyGuidedPlanStepPreset(GuidedSessionPlanStep step) {
    final gameId = step.gameId.trim();
    if (gameId.isEmpty) {
      return;
    }

    if (_selectedGameId != gameId && mounted) {
      setState(() {
        _selectedGameId = gameId;
      });
    } else if (_selectedGameId != gameId) {
      _selectedGameId = gameId;
    }

    final preset = step.configPreset;
    if (preset.isEmpty) {
      return;
    }

    final schema = _selectedGameEntry.mobileControlSchema;
    if (schema != null) {
      _ensureDynamicControlValuesForSelectedSchema();
      for (final control in schema.controls) {
        if (!preset.containsKey(control.controlId)) {
          continue;
        }
        _dynamicControlValuesByControlId[control.controlId] =
            _coerceSchemaValue(
          control.binding.valueType,
          preset[control.controlId],
        );
      }
      return;
    }

    if (_selectedGameId == _demoCubeGameId) {
      final cubeCount = _tryReadIntPreset(preset, const ['cubeCount']);
      final cubeSpeed = _tryReadDoublePreset(preset, const ['cubeSpeed']);
      final levelMode = _tryReadStringPreset(preset, const ['levelMode']);
      if (mounted) {
        setState(() {
          if (cubeCount != null) {
            _demoCubeCount = cubeCount.clamp(4, 64).toInt();
          }
          if (cubeSpeed != null) {
            _demoCubeSpeed = cubeSpeed.clamp(0.2, 3.0).toDouble();
          }
          if (levelMode != null && _demoLevelModes.contains(levelMode)) {
            _demoLevelMode = levelMode;
          }
        });
      }
      return;
    }

    if (_selectedGameId == _pulseTargetGameId && mounted) {
      final targetCount = _tryReadIntPreset(preset, const ['targetCount']);
      final targetSpeed = _tryReadDoublePreset(preset, const ['targetSpeed']);
      final targetScale = _tryReadDoublePreset(preset, const ['targetScale']);
      setState(() {
        if (targetCount != null) {
          _pulseTargetCount = targetCount.clamp(2, 64).toInt();
        }
        if (targetSpeed != null) {
          _pulseTargetSpeed = targetSpeed.clamp(0.2, 3.0).toDouble();
        }
        if (targetScale != null) {
          _pulseTargetScale = targetScale.clamp(0.1, 1.5).toDouble();
        }
      });
    }
  }

  int? _tryReadIntPreset(Map<String, dynamic> preset, List<String> keys) {
    for (final key in keys) {
      if (!preset.containsKey(key)) {
        continue;
      }
      final value = preset[key];
      if (value is int) {
        return value;
      }
      if (value is num) {
        return value.round();
      }
      if (value is String) {
        final parsed = int.tryParse(value.trim());
        if (parsed != null) {
          return parsed;
        }
      }
    }
    return null;
  }

  double? _tryReadDoublePreset(Map<String, dynamic> preset, List<String> keys) {
    for (final key in keys) {
      if (!preset.containsKey(key)) {
        continue;
      }
      final value = preset[key];
      if (value is double) {
        return value;
      }
      if (value is num) {
        return value.toDouble();
      }
      if (value is String) {
        final parsed = double.tryParse(value.trim());
        if (parsed != null) {
          return parsed;
        }
      }
    }
    return null;
  }

  String? _tryReadStringPreset(Map<String, dynamic> preset, List<String> keys) {
    for (final key in keys) {
      if (!preset.containsKey(key)) {
        continue;
      }
      final value = preset[key]?.toString().trim() ?? '';
      if (value.isNotEmpty) {
        return value;
      }
    }
    return null;
  }

  Future<bool> _applyGuidedContinuationPolicyForUnfinishedSession() async {
    final persisted = _latestPersistedSession;
    if (persisted == null || !persisted.requiresHandoffDecision) {
      return true;
    }

    final persistedSessionId = persisted.sessionId.trim();
    if (persistedSessionId.isEmpty ||
        _wasSessionRecentlyEnded(persistedSessionId)) {
      return true;
    }

    final activeSessionId = _activeSessionId.trim();
    if (activeSessionId == persistedSessionId) {
      return true;
    }

    final policy = _therapistSessionSettings.guidedSessionContinuationPolicy;
    final decision = GuidedSessionContinuationEvaluator.evaluate(
      policy: policy,
      hasUnfinishedSession: persisted.requiresHandoffDecision,
      sessionRecentlyEnded: _wasSessionRecentlyEnded(persistedSessionId),
      sessionState: persisted.state,
      nowUtc: DateTime.now().toUtc(),
      interruptedAtUtc: persisted.interruptedAtUtc,
      recoveryWindowMinutes:
          _therapistSessionSettings.sessionRecoveryWindowMinutes,
    );
    if (decision != GuidedSessionContinuationDecision.autoContinueUnfinished) {
      return true;
    }

    _logSessionDecision(
      source: 'parent_guided',
      decision: 'AUTO_CONTINUE_UNFINISHED',
      sessionId: persistedSessionId,
      reason: 'GUIDED_CONTINUATION_${policy.wireValue.toUpperCase()}',
    );

    if (mounted) {
      setState(() {
        // Mobile-only arch guard: only adopt mobile-* IDs as active session.
        if (persistedSessionId.startsWith('mobile-')) {
          _activeSessionId = persistedSessionId;
        }
        _sessionAttachReady = false;
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
    } else {
      if (persistedSessionId.startsWith('mobile-')) {
        _activeSessionId = persistedSessionId;
      }
      _sessionAttachReady = false;
      _requiresSessionDecision = false;
      _remoteSessionIdPendingDecision = null;
    }

    await _ensureSessionAttached(
      reasonCode: 'GUIDED_CONTINUATION_POLICY',
      force: true,
      sessionIdOverride: persistedSessionId,
    );

    if (!_sessionAttachReady && mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content:
              Text('Waiting for headset session attach before guided resume.'),
          backgroundColor: Colors.orange,
        ),
      );
    }

    return _sessionAttachReady;
  }

  Future<void> _startParentGuidedSession() async {
    if (!_isParentRole) {
      return;
    }

    final planBanner = _planGateBannerText;
    if (_isPlanBlockingLaunch || planBanner != null) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(planBanner ?? 'Current plan blocks guided start.'),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (!mounted || !_isConnected || !_sessionAttachReady) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Wait for headset connection and session sync first.'),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (_isHeadsetPresenceBlocking) {
      if (!mounted) {
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Headset is not in active VR app yet.'),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    final guidedStep = _resolveDefaultGuidedPlanStep();
    if (guidedStep == null) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Guided plan is empty. Therapist must configure guided plan steps first.',
            ),
            backgroundColor: Colors.orange,
          ),
        );
      }
      return;
    }
    _applyGuidedPlanStepPreset(guidedStep);

    final continuationApplied =
        await _applyGuidedContinuationPolicyForUnfinishedSession();
    if (!continuationApplied) {
      return;
    }

    if (_workflowStep != _WorkflowStep.gameSetup) {
      setState(() {
        _workflowStep = _WorkflowStep.gameSetup;
        _isVideoPreviewExpanded = true;
      });
      unawaited(
        _persistWorkflowCheckpoint(
          workflowStep: _WorkflowStep.gameSetup,
          reasonCode: 'SCREEN_GAME_SETUP',
        ),
      );
    }

    await _startFromSetup();
  }

  Future<void> _startFromSetup() async {
    if (_isGameRuntimeActive) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Gra jest aktywna. Najpierw zatrzymaj lub zakoncz sesje, aby zmienic ustawienia.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (!_isSelectedGamePreparedOnHeadset) {
      await _prepareSelectedGameScene(
        reasonCode: 'START_REQUEST_SCENE_NOT_READY',
      );

      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Quest is still loading the selected game scene. Wait a moment and tap Start again.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    final selectedContentState = _selectedContentState;
    if (!_isLaunchableContentState(selectedContentState)) {
      if (!mounted) {
        return;
      }

      final launchReadinessHint = _buildLaunchReadinessHint(
        _selectedGameEntry,
        selectedContentState,
      );
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(launchReadinessHint),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    await _runPrimaryAction(() async {
      await _sendCommand(
        CriticalCommandIds.startGame,
        extraPayload: const <String, dynamic>{
          'resumeFromSaved': false,
        },
      );
    });
  }

  Future<void> _restartFromSetup() async {
    if (!_isGameRuntimeActive) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Restart is available only while a game is active.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) {
            return AlertDialog(
              title: const Text('Restart game?'),
              content: const Text(
                'Current round will be stopped and started from the beginning. Continue?',
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.of(context).pop(false),
                  child: const Text('Cancel'),
                ),
                ElevatedButton(
                  onPressed: () => Navigator.of(context).pop(true),
                  child: const Text('Restart'),
                ),
              ],
            );
          },
        ) ??
        false;

    if (!confirmed) {
      return;
    }

    await _runPrimaryAction(() async {
      final stopSent = await _sendCommand(
        CriticalCommandIds.stopGame,
        extraPayload: const <String, dynamic>{
          'reason': 'RESTART_GAME',
        },
        showSuccessSnack: false,
      );
      if (!stopSent) {
        return;
      }

      await _sendCommand(
        CriticalCommandIds.startGame,
        extraPayload: const <String, dynamic>{
          'resumeFromSaved': false,
        },
      );
    });
  }

  Future<void> _stopRoundFromSetup() async {
    if (!_isGameRuntimeActive) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Stop is available only while a game is active.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    await _runPrimaryAction(() async {
      await _sendCommand(
        CriticalCommandIds.stopGame,
        extraPayload: const <String, dynamic>{
          'reason': 'THERAPIST_RECONFIGURE',
        },
        showSuccessSnack: false,
      );
    });
  }

  Future<void> _returnToGameCatalog() async {
    final shouldForceLocalCatalogReturn =
        !_isConnected || _isHeadsetPresenceBlocking;
    if (shouldForceLocalCatalogReturn) {
      if (!mounted) {
        return;
      }
      setState(() {
        _workflowStep = _WorkflowStep.gameCatalog;
        _isVideoPreviewExpanded = false;
        _optimisticRuntimeActive = false;
        _optimisticRuntimePaused = false;
        _optimisticPreparedGameId = null;
      });
      _clearReconnectGameRuntimeRecovery(reasonCode: 'RETURN_TO_CATALOG');
      unawaited(
        _persistWorkflowCheckpoint(
          workflowStep: _WorkflowStep.gameCatalog,
          reasonCode: 'SCREEN_GAME_CATALOG',
        ),
      );
      return;
    }

    final shouldConfirmCatalogReturn =
        _workflowStep == _WorkflowStep.gameSetup && _selectedGameId.isNotEmpty;
    var shouldRouteQuestBackToMenu = false;
    if (shouldConfirmCatalogReturn) {
      final decision = await showDialog<bool>(
            context: context,
            barrierDismissible: false,
            builder: (context) {
              return PopScope(
                canPop: false,
                child: AlertDialog(
                  title: const Text('Return to game catalog?'),
                  content: Text(
                    _isGameRuntimeActive
                        ? 'This will end the current round, fade back to the main scene on Quest, and return to the game catalog. Continue?'
                        : 'This will leave ${_selectedGameEntry.title}, return Quest to the main scene, and open the game catalog. Continue?',
                  ),
                  actions: [
                    TextButton(
                      onPressed: () => Navigator.of(context).pop(false),
                      child: const Text('Cancel'),
                    ),
                    ElevatedButton(
                      onPressed: () => Navigator.of(context).pop(true),
                      child: const Text('Return'),
                    ),
                  ],
                ),
              );
            },
          ) ??
          false;

      if (!decision) {
        return;
      }

      shouldRouteQuestBackToMenu = _isConnected && _sessionAttachReady;
    }

    await _runPrimaryAction(() async {
      if (shouldRouteQuestBackToMenu) {
        final stopSent = await _sendCommand(
          CriticalCommandIds.stopGame,
          extraPayload: const <String, dynamic>{
            'reason': 'RETURN_TO_CATALOG',
          },
          showSuccessSnack: false,
        );
        if (!stopSent) {
          return;
        }
      }

      if (!mounted) {
        return;
      }

      setState(() {
        _workflowStep = _WorkflowStep.gameCatalog;
        _isVideoPreviewExpanded = false;
      });
      _clearReconnectGameRuntimeRecovery(reasonCode: 'RETURN_TO_CATALOG');
      unawaited(
        _persistWorkflowCheckpoint(
          workflowStep: _WorkflowStep.gameCatalog,
          reasonCode: 'SCREEN_GAME_CATALOG',
        ),
      );
    });
  }

  Future<bool> _sendEndSessionWithConfirmation({
    String reasonCode = 'THERAPIST_CONFIRMED_END',
    Map<String, dynamic>? extraPayload,
    bool allowLocalFallbackOnTransportFailure = false,
  }) async {
    final sessionIdToEnd = _resolveSessionIdForCriticalCommand(
      CriticalCommandIds.endSession,
    );

    if (!_isConnected) {
      if (allowLocalFallbackOnTransportFailure) {
        await _applyLocalEndSessionFallback(
          sessionIdToEnd: sessionIdToEnd,
          reasonCode: reasonCode,
          failureReasonCode: 'DISCONNECTED',
        );
        return true;
      }
      if (!mounted) {
        return false;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content:
              Text('Device disconnected. Reconnect before ending session.'),
          backgroundColor: Colors.orange,
        ),
      );
      return false;
    }

    if (allowLocalFallbackOnTransportFailure && _isHeadsetPresenceBlocking) {
      final presenceReasonCode =
          _lastDevicePresenceSignal?.reasonCode.trim().toUpperCase() ?? '';
      await _applyLocalEndSessionFallback(
        sessionIdToEnd: sessionIdToEnd,
        reasonCode: reasonCode,
        failureReasonCode:
            presenceReasonCode.isEmpty ? 'APP_FOCUS_LOST' : presenceReasonCode,
      );
      return true;
    }

    _logAttachDecision(
      decision: 'END_SESSION_REQUEST',
      reasonCode: reasonCode,
      commandId: CriticalCommandIds.endSession,
      sessionId: sessionIdToEnd,
    );
    final payload = <String, dynamic>{
      'reasonCode': reasonCode,
      ...?extraPayload,
    };
    final ended = await _sendCommand(
      CriticalCommandIds.endSession,
      extraPayload: payload,
      showSuccessSnack: false,
    );
    if (!ended) {
      final failureReasonCode =
          _lastCriticalFailureReasonByCommand[CriticalCommandIds.endSession] ??
              'UNSPECIFIED';
      if (allowLocalFallbackOnTransportFailure &&
          _shouldApplyLocalEndSessionFallback(failureReasonCode)) {
        await _applyLocalEndSessionFallback(
          sessionIdToEnd: sessionIdToEnd,
          reasonCode: reasonCode,
          failureReasonCode: failureReasonCode,
        );
        return true;
      }
      return false;
    }

    _markSessionAsRecentlyEnded(sessionIdToEnd);
    _clearDeferredHandoff(
      sessionId: sessionIdToEnd,
      source: 'command',
      reasonCode: reasonCode,
    );
    _markSessionAsRecentlyEnded(_lastSessionStateUpdateSessionId);
    _markSessionAsRecentlyEnded(_lastRuntimeStatusSessionId);
    _markSessionAsRecentlyEnded(_remoteSessionIdPendingDecision);
    _markSessionAsRecentlyEnded(_latestPersistedSession?.sessionId);

    if (mounted) {
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
    }
    return true;
  }

  Future<void> _applyLocalEndSessionFallback({
    required String sessionIdToEnd,
    required String reasonCode,
    required String failureReasonCode,
  }) async {
    final normalizedSessionId = sessionIdToEnd.trim();
    if (normalizedSessionId.isEmpty) {
      return;
    }
    final normalizedFailureReason = failureReasonCode.trim().isEmpty
        ? 'UNSPECIFIED'
        : failureReasonCode.trim().toUpperCase();

    await _persistCommandSideEffects(
      CriticalCommandIds.endSession,
      sessionIdOverride: normalizedSessionId,
      extraPayload: <String, dynamic>{
        'reasonCode': reasonCode,
        'origin': 'local_end_session_fallback',
        'remoteAckReceived': false,
        'remoteFailureReasonCode': normalizedFailureReason,
      },
    );

    _markSessionAsRecentlyEnded(normalizedSessionId);
    _clearDeferredHandoff(
      sessionId: normalizedSessionId,
      source: 'command',
      reasonCode: reasonCode,
    );
    _markSessionAsRecentlyEnded(_lastSessionStateUpdateSessionId);
    _markSessionAsRecentlyEnded(_lastRuntimeStatusSessionId);
    _markSessionAsRecentlyEnded(_remoteSessionIdPendingDecision);
    _markSessionAsRecentlyEnded(_latestPersistedSession?.sessionId);

    if (mounted) {
      setState(() {
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Headset did not confirm END_SESSION. Applied local closure fallback.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
    }

    debugPrint(
      '[ControlScreen] Local END_SESSION fallback applied: '
      'session=$normalizedSessionId reason=$reasonCode '
      'failure=$normalizedFailureReason',
    );
  }

  Future<bool> _handleSystemBackPressed() async {
    if (_allowSystemPop) {
      return true;
    }

    if (_isPrimaryActionInFlight) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Wait for the current action to finish.'),
            backgroundColor: Colors.orange,
          ),
        );
      }
      return false;
    }

    final choice = await showDialog<_ExitChoice>(
      context: context,
      barrierDismissible: false,
      builder: (context) {
        return PopScope(
          canPop: false,
          child: AlertDialog(
            title: const Text('Close controller?'),
            content: const Text(
              'Do you want to end the session now, or leave it unfinished and exit?',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(_ExitChoice.cancel),
                child: const Text('Cancel'),
              ),
              TextButton(
                onPressed: () =>
                    Navigator.of(context).pop(_ExitChoice.keepUnfinished),
                child: const Text('Leave unfinished'),
              ),
              ElevatedButton(
                onPressed: () =>
                    Navigator.of(context).pop(_ExitChoice.endSession),
                child: const Text('End session'),
              ),
            ],
          ),
        );
      },
    );

    if (choice == null || choice == _ExitChoice.cancel) {
      return false;
    }

    if (choice == _ExitChoice.endSession) {
      await _endSessionAndReturnToStudentSelection();
      return false;
    }

    await _disconnectAndPop(
      returnToStudentSelection: choice == _ExitChoice.endSession,
    );
    return false;
  }

  Future<bool> _endSessionAndReturnToStudentSelection() async {
    var ended = false;
    await _runPrimaryAction(() async {
      ended = await _sendEndSessionWithConfirmation(
        allowLocalFallbackOnTransportFailure: true,
      );
      if (!ended) {
        return;
      }

      await _disconnectAndPop(returnToStudentSelection: true);
    });
    return ended;
  }

  Future<void> _disconnectAndPop(
      {bool returnToStudentSelection = false}) async {
    _autoReconnectEnabled = false;
    _allowSystemPop = true;
    unawaited(ForegroundServiceBridge.stop());

    // Delete Firestore session if no game was played (therapist ended without playing).
    if (returnToStudentSelection && !_gameDataService.hasGameData) {
      await _gameDataService.deleteSessionIfEmpty();
    }

    await _connection.disconnect();

    if (!mounted) {
      return;
    }

    Navigator.pop(context, returnToStudentSelection);
  }

  @override
  Widget build(BuildContext context) {
    final isCatalogScreen = _workflowStep == _WorkflowStep.gameCatalog;
    final connectedReady = _isControlLinkReadyForCommands;
    final connectedDegraded = _isConnected && !connectedReady;
    final connectionBadgeLabel = _connectionBadgeLabel;
    final connectionBadgeIcon = _connectionBadgeIcon;
    final connectionBadgeTooltip = connectedReady
        ? 'Control link is ready for commands.'
        : _controlLinkBlockedHint;

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (didPop) {
          return;
        }

        unawaited(_handleSystemBackPressed());
      },
      child: Scaffold(
        appBar: AppBar(
          automaticallyImplyLeading: false,
          leading: isCatalogScreen
              ? IconButton(
                  onPressed: _isPrimaryActionInFlight
                      ? null
                      : () => unawaited(_endSessionFromCatalog()),
                  tooltip: 'End session and return to student selection',
                  icon: const Icon(Icons.arrow_back),
                )
              : IconButton(
                  onPressed: _isPrimaryActionInFlight
                      ? null
                      : () => unawaited(_returnToGameCatalog()),
                  tooltip: 'Back to game catalog',
                  icon: const Icon(Icons.arrow_back),
                ),
          title: Text(
            isCatalogScreen
                ? '${widget.student.firstName}: game catalog'
                : '${_selectedGameEntry.title}: session',
          ),
          actions: [
            if (_hasDeferredHandoff) _buildDeferredHandoffBadgeAction(),
            Padding(
              padding: const EdgeInsets.only(right: 12),
              child: Tooltip(
                message: connectionBadgeTooltip,
                child: Center(
                  child: Container(
                    padding:
                        const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
                    decoration: BoxDecoration(
                      color: connectedReady
                          ? Colors.green.shade50
                          : (connectedDegraded
                              ? Colors.orange.shade50
                              : Colors.red.shade50),
                      borderRadius: BorderRadius.circular(999),
                      border: Border.all(
                        color: connectedReady
                            ? Colors.green.shade200
                            : (connectedDegraded
                                ? Colors.orange.shade200
                                : Colors.red.shade200),
                      ),
                    ),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(
                          connectionBadgeIcon,
                          color: connectedReady
                              ? Colors.green.shade700
                              : (connectedDegraded
                                  ? Colors.orange.shade800
                                  : Colors.red.shade700),
                          size: 16,
                        ),
                        const SizedBox(width: 6),
                        Text(
                          connectionBadgeLabel,
                          style: TextStyle(
                            color: connectedReady
                                ? Colors.green.shade800
                                : (connectedDegraded
                                    ? Colors.orange.shade900
                                    : Colors.red.shade800),
                            fontSize: 12,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
        body: SafeArea(
          child: Stack(
            children: [
              Positioned.fill(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(12, 8, 12, 12),
                  child: IndexedStack(
                    index: isCatalogScreen ? 0 : 1,
                    children: [
                      _buildGameCatalogStep(),
                      _buildGameSetupStep(),
                    ],
                  ),
                ),
              ),
              _buildSessionTimelineDrawerOverlay(),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildVideoPreviewPanel({required String subtitle}) {
    return Card(
      margin: EdgeInsets.zero,
      child: Column(
        children: [
          ListTile(
            contentPadding: const EdgeInsets.symmetric(
              horizontal: 12,
              vertical: 2,
            ),
            title: const Text(
              'VR preview',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            subtitle: Text(
              subtitle,
              style: TextStyle(
                color: Colors.grey[700],
                fontSize: 12,
              ),
            ),
            trailing: IconButton(
              icon: Icon(
                _isVideoPreviewExpanded ? Icons.expand_less : Icons.expand_more,
              ),
              onPressed: () {
                setState(() {
                  _isVideoPreviewExpanded = !_isVideoPreviewExpanded;
                });
              },
            ),
          ),
          Offstage(
            offstage: !_isVideoPreviewExpanded,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
              child: AspectRatio(
                aspectRatio: 16 / 9,
                child: MediaStreamWidget(
                  connection: _connection,
                  deviceIP: widget.device.ip,
                  port: widget.device.videoPort,
                  previewActive: _isPreviewStreamExpected,
                  onStateChanged: _handleMediaPreviewStateChanged,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildStateBanner({
    required IconData icon,
    required Color color,
    required String text,
  }) {
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.1),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: color.withValues(alpha: 0.25)),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 16, color: color),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              text,
              style: TextStyle(
                color: color,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildDeferredHandoffBadgeAction() {
    final sessionId = _deferredHandoffSessionIdValue;
    final hasDeferred = sessionId.isNotEmpty;
    final accent = Colors.orange.shade800;

    return IconButton(
      tooltip: hasDeferred
          ? 'Deferred handoff pending. Tap to review.'
          : 'No deferred handoff',
      onPressed: hasDeferred ? () => unawaited(_reviewDeferredHandoff()) : null,
      icon: Stack(
        clipBehavior: Clip.none,
        children: [
          const Icon(Icons.assignment_late_outlined),
          if (hasDeferred)
            Positioned(
              right: -1,
              top: -1,
              child: Container(
                width: 11,
                height: 11,
                decoration: BoxDecoration(
                  color: accent,
                  shape: BoxShape.circle,
                  border: Border.all(color: Colors.white, width: 1),
                ),
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildDeferredHandoffBanner() {
    final sessionId = _deferredHandoffSessionIdValue;
    final shortSessionId = sessionId.length > 16
        ? sessionId.substring(sessionId.length - 16)
        : sessionId;
    final reasonLabel =
        _describeDeferredHandoffReason(_deferredHandoffReasonCode ?? '');
    final deferredAt = _deferredHandoffMarkedAtUtc;
    final deferredAtLabel = _formatTimelineTimestamp(deferredAt);

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: Colors.orange.shade50,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.orange.shade200),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            Icons.assignment_late_outlined,
            size: 16,
            color: Colors.orange.shade800,
          ),
          const SizedBox(width: 8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Deferred handoff pending',
                  style: TextStyle(
                    color: Colors.orange.shade900,
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  'Unfinished session [$shortSessionId] was deferred due to '
                  '$reasonLabel at $deferredAtLabel.',
                  style: TextStyle(
                    color: Colors.orange.shade900,
                    fontSize: 12,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          OutlinedButton(
            onPressed: () => unawaited(_reviewDeferredHandoff()),
            style: OutlinedButton.styleFrom(
              foregroundColor: Colors.orange.shade900,
              side: BorderSide(color: Colors.orange.shade300),
              visualDensity: VisualDensity.compact,
            ),
            child: const Text('Review'),
          ),
        ],
      ),
    );
  }

  String _resolveTimelineSessionId() {
    // In the mobile-only architecture _activeSessionId is set in initState
    // and always points to the mobile-* document — prefer it first so events
    // like CONTROLLER_DISCONNECTED and SESSION_ENDED never land in a UUID doc.
    final activeSessionId = _activeSessionId.trim();
    if (activeSessionId.isNotEmpty) return activeSessionId;

    final lastSessionSignalId = _lastSessionStateUpdateSessionId?.trim() ?? '';
    final lastRuntimeSignalId = _lastRuntimeStatusSessionId?.trim() ?? '';
    final pendingDecisionSessionId =
        _remoteSessionIdPendingDecision?.trim() ?? '';
    final persistedSessionId = _latestPersistedSession?.sessionId.trim() ?? '';

    for (final candidate in <String>[
      lastSessionSignalId,
      lastRuntimeSignalId,
      pendingDecisionSessionId,
      persistedSessionId,
    ]) {
      if (candidate.isNotEmpty) {
        return candidate;
      }
    }

    return '';
  }

  Future<void> _appendTimelineNote({
    required String noteText,
    required bool fromQuickTemplate,
  }) async {
    final normalizedNoteText = noteText.trim();
    if (normalizedNoteText.isEmpty || _timelineNoteInFlight) {
      return;
    }

    final sessionId = _resolveTimelineSessionId();
    if (sessionId.isEmpty) {
      if (!mounted) {
        return;
      }

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Session context is not ready yet. Wait for sync before adding notes.',
          ),
          backgroundColor: Colors.orange,
        ),
      );
      return;
    }

    if (mounted) {
      setState(() {
        _timelineNoteInFlight = true;
      });
    } else {
      _timelineNoteInFlight = true;
    }

    try {
      await SessionJournalService.appendTherapistTimelineNote(
        sessionId: sessionId,
        studentId: widget.student.id,
        therapistId: _resolveActorTherapistId(),
        noteText: normalizedNoteText,
        gameId: _selectedGameId,
        fromQuickTemplate: fromQuickTemplate,
      );
      if (!mounted) {
        return;
      }

      if (!fromQuickTemplate) {
        _timelineNoteController.clear();
      }

      unawaited(
        _refreshSessionJournal(
          reason: 'THERAPIST_NOTE_SAVED',
          sessionIdOverride: sessionId,
        ),
      );

      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Timeline note saved.'),
          duration: Duration(seconds: 1),
        ),
      );
    } catch (e) {
      if (!mounted) {
        return;
      }

      _enqueueIncidentAlert(
        title: 'Could not save timeline note',
        message: 'Could not save timeline note: $e',
        reasonCode: OpsErrorCatalog.tryExtractReasonCode(e) ?? 'UNSPECIFIED',
        severity: OperatorIncidentSeverity.error,
      );
    } finally {
      if (mounted) {
        setState(() {
          _timelineNoteInFlight = false;
        });
      } else {
        _timelineNoteInFlight = false;
      }
    }
  }

  String _formatTimelineTimestamp(DateTime? eventAtUtc) {
    if (eventAtUtc == null) {
      return '--:--:--';
    }

    final local = eventAtUtc.toLocal();
    final nowLocal = DateTime.now();
    final hour = local.hour.toString().padLeft(2, '0');
    final minute = local.minute.toString().padLeft(2, '0');
    final second = local.second.toString().padLeft(2, '0');
    final month = local.month.toString().padLeft(2, '0');
    final day = local.day.toString().padLeft(2, '0');
    final isSameDay = local.year == nowLocal.year &&
        local.month == nowLocal.month &&
        local.day == nowLocal.day;

    return isSameDay
        ? '$hour:$minute:$second'
        : '$month-$day $hour:$minute:$second';
  }

  String _formatTimelineEventType(String eventType) {
    switch (eventType.trim()) {
      case SessionJournalService.therapistTimelineNoteEventType:
        return 'Therapist note';
      case 'CONTROLLER_CONNECTED':
        return 'Controller connected';
      case 'CONTROLLER_RECONNECTED':
        return 'Controller reconnected';
      case 'CONTROLLER_DISCONNECTED':
        return 'Controller disconnected';
      case 'CONTROLLER_RECONNECT_ATTEMPT':
        return 'Controller reconnect attempt';
      case 'CONTROLLER_RECONNECT_SUCCESS':
        return 'Controller reconnect success';
      case 'CONTROLLER_RECONNECT_FAILED':
        return 'Controller reconnect failed';
      case 'SESSION_ATTACH_ATTEMPT':
        return 'Session attach attempt';
      case 'SESSION_ATTACH_SUCCEEDED':
        return 'Session attach succeeded';
      case 'SESSION_ATTACH_FAILED':
        return 'Session attach failed';
      case 'SESSION_ATTACH_ACK':
        return 'Session attached';
      case 'VR_DEVICE_PRESENCE_UPDATE':
        return 'Headset lifecycle update';
      case 'VR_DEVICE_CONNECTED':
        return 'Quest connected';
      case 'VR_APP_FOREGROUND':
        return 'Quest app foreground';
      case 'VR_APP_BACKGROUND':
        return 'Quest app background';
      case 'OBJECT_INTERACTION':
        return 'Object interaction';
      case 'VR_FOCUS_LOST':
        return 'Quest focus lost';
      case 'VR_FOCUS_GAINED':
        return 'Quest focus gained';
      case 'VR_APP_QUITTING':
        return 'Quest app quitting';
      case 'MOBILE_LIFECYCLE_STATE':
        return 'Mobile lifecycle update';
      case 'RUNTIME_SESSION_STATE_UPDATE':
        return 'Session state update';
      case 'GAME_STARTED':
        return 'Game started';
      case 'GAME_RESUMED':
        return 'Game resumed';
      case 'GAME_PAUSED':
        return 'Game paused';
      case 'GAME_ENDED':
        return 'Game ended';
      case 'SESSION_ENDED':
        return 'Session ended';
      case 'SESSION_ENDED_BY_DECISION':
        return 'Session ended by decision';
      case 'INTERRUPTED_SESSION_AUTO_CLOSED':
        return 'Interrupted session auto-closed';
      case 'SESSION_HANDOFF_DEFERRED':
        return 'Handoff deferred';
      case 'SESSION_HANDOFF_DEFERRED_CLEARED':
        return 'Handoff defer cleared';
      default:
        final compact = eventType.trim();
        if (compact.isEmpty) {
          return 'Unknown event';
        }
        final words = compact
            .split('_')
            .where((entry) => entry.trim().isNotEmpty)
            .map((entry) {
          final lower = entry.toLowerCase();
          return '${lower[0].toUpperCase()}${lower.substring(1)}';
        });
        return words.join(' ');
    }
  }

  String _formatTimelineEventSummary(SessionTimelineEvent event) {
    if (event.isTherapistNote) {
      return event.noteText;
    }

    final details = event.details;
    if (event.eventType.trim() == 'OBJECT_INTERACTION') {
      final fragments = <String>[];
      final targetName = (details['targetName'] as String? ?? '').trim();
      final targetCategory = (details['targetCategory'] as String? ?? '').trim();
      final interactionEventType =
          (details['interactionEventType'] as String? ?? '').trim();
      final actionOutcome =
          (details['actionOutcome'] as String? ?? '').trim().toUpperCase();
      final reasonCode = (details['reasonCode'] as String? ?? '').trim();

      if (actionOutcome == 'CORRECT') {
        fragments.add('Correct');
      } else if (actionOutcome == 'INCORRECT') {
        fragments.add('Incorrect');
      }

      if (targetName.isNotEmpty) {
        fragments.add(targetName);
      } else if (targetCategory.isNotEmpty) {
        fragments.add(targetCategory.toLowerCase().replaceAll('_', ' '));
      } else if (interactionEventType.isNotEmpty) {
        fragments.add(interactionEventType.toLowerCase().replaceAll('_', ' '));
      }

      final responseSec = _resolveTimelineResponseSec(
        details: details,
        eventAtUtc: event.eventAtUtc,
      );
      if (responseSec != null && responseSec > 0) {
        final precision = responseSec >= 10 ? 0 : 1;
        fragments.add('after ${responseSec.toStringAsFixed(precision)}s');
      }

      if (reasonCode.isNotEmpty) {
        final knownTag = OpsErrorCatalog.tryBuildKnownReasonTag(reasonCode);
        fragments.add('reason=${knownTag ?? reasonCode}');
      }

      return fragments.join(' | ');
    }

    final fragments = <String>[];
    for (final key in <String>[
      'state',
      'presenceState',
      'lifecycleState',
      'reasonCode',
      'reason',
      'decision',
    ]) {
      final value = details[key];
      final text = value?.toString().trim() ?? '';
      if (text.isEmpty) {
        continue;
      }
      if (key == 'reasonCode') {
        final knownTag = OpsErrorCatalog.tryBuildKnownReasonTag(text);
        final reasonSummary = knownTag ?? text;
        fragments.add('$key=$reasonSummary');
        continue;
      }
      fragments.add('$key=$text');
    }

    final gameConfigType = (details['gameConfigType'] as String? ?? '').trim();
    final gameConfigVersion = _asTimelineInt(details['gameConfigVersion']);
    final gameConfigSummary =
        (details['gameConfigSummary'] as String? ?? '').trim();
    if (gameConfigType.isNotEmpty) {
      final versionSuffix = gameConfigVersion > 0 ? ' v$gameConfigVersion' : '';
      fragments.add('config=$gameConfigType$versionSuffix');
    }
    if (gameConfigSummary.isNotEmpty) {
      fragments.add('settings=$gameConfigSummary');
    }

    if (fragments.isEmpty) {
      return '';
    }

    return fragments.join(' | ');
  }

  int _asTimelineInt(dynamic value) {
    if (value is int) {
      return value;
    }
    if (value is num) {
      return value.toInt();
    }
    if (value is String) {
      return int.tryParse(value.trim()) ?? 0;
    }
    return 0;
  }

  double? _asTimelineDouble(dynamic value) {
    if (value is double) {
      return value.isFinite ? value : null;
    }
    if (value is int) {
      return value.toDouble();
    }
    if (value is num) {
      final resolved = value.toDouble();
      return resolved.isFinite ? resolved : null;
    }
    if (value is String) {
      final parsed = double.tryParse(value.trim());
      return parsed != null && parsed.isFinite ? parsed : null;
    }
    return null;
  }

  double? _resolveTimelineResponseSec({
    required Map<String, dynamic> details,
    required DateTime? eventAtUtc,
  }) {
    final explicit = _asTimelineDouble(details['responseSec']);
    if (explicit != null) {
      return explicit;
    }

    final appearedAtRaw = (details['targetAppearedAtUtc'] as String? ?? '').trim();
    if (appearedAtRaw.isEmpty || eventAtUtc == null) {
      return null;
    }

    final appearedAt = DateTime.tryParse(appearedAtRaw)?.toUtc();
    if (appearedAt == null) {
      return null;
    }

    final deltaMs =
        eventAtUtc.toUtc().millisecondsSinceEpoch - appearedAt.millisecondsSinceEpoch;
    if (deltaMs < 0) {
      return 0;
    }

    return deltaMs / 1000.0;
  }

  Widget _buildTimelineEventTile(SessionTimelineEvent event) {
    final title = _formatTimelineEventType(event.eventType);
    final summary = _formatTimelineEventSummary(event);
    final timestamp = _formatTimelineTimestamp(event.eventAtUtc);
    final metaFragments = <String>[timestamp];
    if (event.source.trim().isNotEmpty) {
      metaFragments.add('source=${event.source.trim()}');
    }
    if (event.gameId.trim().isNotEmpty) {
      metaFragments.add('game=${_resolveCatalogGameTitle(event.gameId.trim())}');
    }
    final metaLine = metaFragments.join(' | ');

    final isNote = event.isTherapistNote;
    final interactionOutcome = event.eventType.trim() == 'OBJECT_INTERACTION'
        ? (event.details['actionOutcome'] as String? ?? '').trim().toUpperCase()
        : '';
    final markerColor = isNote
        ? Colors.indigo.shade700
        : interactionOutcome == 'CORRECT'
            ? Colors.green.shade700
            : interactionOutcome == 'INCORRECT'
                ? Colors.red.shade700
                : Colors.blueGrey;
    final eventIcon = isNote
        ? Icons.sticky_note_2_outlined
        : interactionOutcome == 'CORRECT'
            ? Icons.check_circle_outline
            : interactionOutcome == 'INCORRECT'
                ? Icons.cancel_outlined
                : Icons.history;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(8),
        border: Border.all(
          color: markerColor.withValues(alpha: 0.25),
        ),
        color: markerColor.withValues(alpha: 0.06),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(
                eventIcon,
                size: 14,
                color: markerColor,
              ),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  title,
                  style: TextStyle(
                    color: markerColor,
                    fontSize: 12,
                    fontWeight: FontWeight.w700,
                  ),
                ),
              ),
            ],
          ),
          if (summary.isNotEmpty) ...[
            const SizedBox(height: 4),
            Text(
              summary,
              style: const TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
          const SizedBox(height: 4),
          Text(
            metaLine,
            style: TextStyle(
              color: Colors.grey.shade700,
              fontSize: 11,
            ),
          ),
        ],
      ),
    );
  }

  // ── VR game-run stats ──────────────────────────────────────────────────────

  Widget _buildVrGameRunsPanel(String sessionId) {
    final allRuns = _loadedJournalSessionId == sessionId
        ? _cachedGameRuns
        : const <GameRunRecord>[];
    final runs = allRuns
        .where((run) => !run.isSummaryPlaceholder)
        .toList(growable: false);
    if (runs.isEmpty) {
      return const SizedBox.shrink();
    }

    final totalInteractions =
        runs.fold(0, (sum, r) => sum + r.interactionCount);
    final totalHits = runs.fold(0, (sum, r) => sum + r.hitCount);
    final totalMisses = runs.fold(0, (sum, r) => sum + r.missCount);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SizedBox(height: 8),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
          decoration: BoxDecoration(
            color: Colors.indigo.shade50,
            borderRadius: BorderRadius.circular(6),
            border: Border.all(color: Colors.indigo.shade100),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  const Icon(Icons.sports_esports,
                      size: 14, color: Colors.indigo),
                  const SizedBox(width: 6),
                  Text(
                    'VR interactions',
                    style: TextStyle(
                      fontSize: 12,
                      fontWeight: FontWeight.w700,
                      color: Colors.indigo.shade700,
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 6),
              Wrap(
                spacing: 8,
                runSpacing: 4,
                children: [
                  _vrStatChip(
                    Icons.check_circle_outline,
                    '$totalHits hits',
                    Colors.green.shade700,
                    Colors.green.shade50,
                  ),
                  _vrStatChip(
                    Icons.cancel_outlined,
                    '$totalMisses misses',
                    Colors.red.shade700,
                    Colors.red.shade50,
                  ),
                  _vrStatChip(
                    Icons.touch_app_outlined,
                    '$totalInteractions total',
                    Colors.blue.shade700,
                    Colors.blue.shade50,
                  ),
                  _vrStatChip(
                    Icons.videogame_asset_outlined,
                    '${runs.length} ${runs.length == 1 ? "game" : "games"}',
                    Colors.purple.shade700,
                    Colors.purple.shade50,
                  ),
                ],
              ),
              if (runs.length > 1) ...[
                const SizedBox(height: 8),
                for (final run in runs) _buildGameRunRow(run),
              ],
            ],
          ),
        ),
        const SizedBox(height: 8),
      ],
    );
  }

  Widget _vrStatChip(
    IconData icon,
    String label,
    Color fgColor,
    Color bgColor,
  ) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
      decoration: BoxDecoration(
        color: bgColor,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 12, color: fgColor),
          const SizedBox(width: 4),
          Text(
            label,
            style: TextStyle(
              fontSize: 11,
              fontWeight: FontWeight.w600,
              color: fgColor,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildGameRunRow(GameRunRecord run) {
    final resolvedTitle = _resolveCatalogGameTitle(run.gameId);
    final gameLabel = resolvedTitle.isNotEmpty
        ? resolvedTitle
        : run.gameId.isNotEmpty
            ? run.gameId
            : _shortTimelineRunId(run.gameRunId);
    final stateIcon = run.isCompleted
        ? Icons.check_circle
        : run.isFailed
            ? Icons.cancel
            : Icons.hourglass_bottom;
    final stateColor = run.isCompleted
        ? Colors.green.shade600
        : run.isFailed
            ? Colors.red.shade600
            : Colors.orange.shade600;

    return Padding(
      padding: const EdgeInsets.only(top: 4),
      child: Row(
        children: [
          Icon(stateIcon, size: 12, color: stateColor),
          const SizedBox(width: 4),
          Expanded(
            child: Text(
              gameLabel,
              style: const TextStyle(fontSize: 11),
              overflow: TextOverflow.ellipsis,
            ),
          ),
          Text(
            '${run.hitCount}/${run.hitCount + run.missCount}',
            style: TextStyle(
              fontSize: 11,
              fontWeight: FontWeight.w600,
              color: Colors.grey.shade700,
            ),
          ),
        ],
      ),
    );
  }

  String _shortTimelineRunId(String runId) {
    final normalized = runId.trim();
    if (normalized.length <= 12) {
      return normalized;
    }
    return '${normalized.substring(0, 8)}...';
  }

  List<SessionTimelineEvent> _visibleTimelineEventsForSession(
    String sessionId, {
    int limit = 40,
  }) {
    final normalizedSessionId = sessionId.trim();
    if (normalizedSessionId.isEmpty) {
      return const <SessionTimelineEvent>[];
    }

    final persisted = _loadedJournalSessionId == normalizedSessionId
        ? _cachedTimelineEvents
        : const <SessionTimelineEvent>[];
    final preview = _liveTimelinePreviewEvents
        .where((event) => event.sessionId.trim() == normalizedSessionId)
        .toList(growable: false);

    if (persisted.isEmpty && preview.isEmpty) {
      return const <SessionTimelineEvent>[];
    }

    final mergedById = <String, SessionTimelineEvent>{};
    for (final event in preview) {
      final key = event.timelineEventId.trim().isNotEmpty
          ? event.timelineEventId.trim()
          : event.eventId.trim();
      if (key.isEmpty) {
        continue;
      }
      mergedById[key] = event;
    }

    for (final event in persisted) {
      final key = event.timelineEventId.trim().isNotEmpty
          ? event.timelineEventId.trim()
          : event.eventId.trim();
      if (key.isEmpty) {
        continue;
      }
      mergedById[key] = event;
    }

    final merged = mergedById.values.toList(growable: false);
    merged.sort((a, b) {
      final timestampCompare = b.eventAtUnixMs.compareTo(a.eventAtUnixMs);
      if (timestampCompare != 0) {
        return timestampCompare;
      }
      return b.eventId.compareTo(a.eventId);
    });

    if (merged.length <= limit) {
      return merged;
    }
    return merged.sublist(0, limit);
  }

  bool get _isTimelineDrawerAvailable {
    if (_isParentRole || _allowSystemPop) {
      return false;
    }

    final sessionId = _resolveTimelineSessionId().trim();
    if (sessionId.isEmpty) {
      return false;
    }

    final persisted = _latestPersistedSession;
    final persistedMatchesActiveSession = persisted != null &&
        persisted.sessionId.trim() == sessionId &&
        !persisted.isTerminal;

    return _sessionAttachReady ||
        _isGameRuntimeActive ||
        _isPrimaryActionInFlight ||
        persistedMatchesActiveSession;
  }

  double _resolveTimelineDrawerWidth(BuildContext context) {
    return MediaQuery.sizeOf(context).width;
  }

  void _toggleTimelineDrawer() {
    if (_timelineDrawerController.value >= 0.5) {
      _closeTimelineDrawer();
      return;
    }

    _openTimelineDrawer();
  }

  void _openTimelineDrawer() {
    final sessionId = _resolveTimelineSessionId().trim();
    if (sessionId.isNotEmpty) {
      unawaited(
        _refreshSessionJournal(
          reason: 'TIMELINE_DRAWER_OPEN',
          sessionIdOverride: sessionId,
        ),
      );
    }

    _timelineDrawerController.animateTo(
      1,
      curve: Curves.easeOutCubic,
    );
  }

  void _closeTimelineDrawer() {
    _timelineDrawerController.animateTo(
      0,
      curve: Curves.easeOutCubic,
    );
  }

  void _handleTimelineDrawerDragUpdate(
    DragUpdateDetails details,
    double panelWidth,
  ) {
    final delta = details.primaryDelta ?? details.delta.dx;
    if (delta == 0 || panelWidth <= 0) {
      return;
    }

    final nextValue = (_timelineDrawerController.value - (delta / panelWidth))
        .clamp(0.0, 1.0);
    _timelineDrawerController.value = nextValue;
  }

  void _handleTimelineDrawerDragEnd(DragEndDetails details) {
    final velocity = details.primaryVelocity ?? 0;
    if (velocity.abs() > 280) {
      if (velocity < 0) {
        _openTimelineDrawer();
      } else {
        _closeTimelineDrawer();
      }
      return;
    }

    if (_timelineDrawerController.value >= 0.5) {
      _openTimelineDrawer();
    } else {
      _closeTimelineDrawer();
    }
  }

  Widget _buildSessionTimelineDrawerOverlay() {
    if (!_isTimelineDrawerAvailable) {
      return const SizedBox.shrink();
    }

    final drawerWidth = _resolveTimelineDrawerWidth(context);
    final screenHeight = MediaQuery.sizeOf(context).height;

    return AnimatedBuilder(
      animation: _timelineDrawerController,
      builder: (context, _) {
        final reveal = _timelineDrawerController.value;
        final handleLeft =
            (drawerWidth - _timelineDrawerHandleHitWidth) * (1 - reveal);
        final handleTop = (screenHeight - _timelineDrawerHandleHeight) / 2;
        final handleAttachedToLeft = reveal >= 0.5;
        return Stack(
          clipBehavior: Clip.none,
          children: [
            if (reveal > 0.01)
              Positioned.fill(
                child: IgnorePointer(
                  ignoring: reveal < 0.05,
                  child: GestureDetector(
                    behavior: HitTestBehavior.opaque,
                    onTap: _closeTimelineDrawer,
                    child: Container(
                      color: Colors.black.withValues(alpha: 0.06 * reveal),
                    ),
                  ),
                ),
              ),
            Positioned(
              right: 0,
              top: 0,
              bottom: 0,
              child: IgnorePointer(
                child: Opacity(
                  opacity: 1 - reveal,
                  child: Container(
                    width: _timelineDrawerCollapsedRailWidth,
                    decoration: BoxDecoration(
                      color: Colors.green.shade200,
                      borderRadius: const BorderRadius.only(
                        topLeft: Radius.circular(999),
                        bottomLeft: Radius.circular(999),
                      ),
                    ),
                  ),
                ),
              ),
            ),
            Positioned.fill(
              child: IgnorePointer(
                ignoring: reveal <= 0.01,
                child: Align(
                  alignment: Alignment.centerRight,
                  child: Transform.translate(
                    offset: Offset(drawerWidth * (1 - reveal), 0),
                    child: SizedBox(
                      width: drawerWidth,
                      height: double.infinity,
                      child: GestureDetector(
                        behavior: HitTestBehavior.translucent,
                        onHorizontalDragUpdate: (details) =>
                            _handleTimelineDrawerDragUpdate(
                                details, drawerWidth),
                        onHorizontalDragEnd: _handleTimelineDrawerDragEnd,
                        child: DecoratedBox(
                          decoration: BoxDecoration(
                            boxShadow: [
                              BoxShadow(
                                color: Colors.black.withValues(alpha: 0.16),
                                blurRadius: 28,
                                offset: const Offset(-6, 12),
                              ),
                            ],
                          ),
                          child: Material(
                            color: Colors.white.withValues(alpha: 0.99),
                            child: _buildTherapistTimelinePanel(inDrawer: true),
                          ),
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
            Positioned(
              left: handleLeft,
              top: handleTop,
              child: GestureDetector(
                behavior: HitTestBehavior.translucent,
                onHorizontalDragUpdate: (details) =>
                    _handleTimelineDrawerDragUpdate(details, drawerWidth),
                onHorizontalDragEnd: _handleTimelineDrawerDragEnd,
                child: SizedBox(
                  width: _timelineDrawerHandleHitWidth,
                  height: _timelineDrawerHandleHeight,
                  child: Align(
                    alignment: handleAttachedToLeft
                        ? Alignment.centerLeft
                        : Alignment.centerRight,
                    child: _buildSessionTimelineDrawerHandle(
                      attachedToLeft: handleAttachedToLeft,
                    ),
                  ),
                ),
              ),
            ),
          ],
        );
      },
    );
  }

  Widget _buildSessionTimelineDrawerHandle({
    required bool attachedToLeft,
  }) {
    final borderRadius = attachedToLeft
        ? const BorderRadius.only(
            topLeft: Radius.circular(8),
            bottomLeft: Radius.circular(8),
            topRight: Radius.circular(999),
            bottomRight: Radius.circular(999),
          )
        : const BorderRadius.only(
            topLeft: Radius.circular(999),
            bottomLeft: Radius.circular(999),
            topRight: Radius.circular(8),
            bottomRight: Radius.circular(8),
          );

    return Material(
      color: Colors.transparent,
      child: InkWell(
        borderRadius: borderRadius,
        onTap: _toggleTimelineDrawer,
        child: Ink(
          width: _timelineDrawerHandleWidth,
          height: _timelineDrawerHandleHeight,
          decoration: BoxDecoration(
            color: Colors.green.shade50.withValues(alpha: 0.98),
            borderRadius: borderRadius,
            border: Border.all(
              color: Colors.green.shade200,
            ),
            boxShadow: [
              BoxShadow(
                color: Colors.black.withValues(alpha: 0.12),
                blurRadius: 16,
                offset: const Offset(-2, 6),
              ),
            ],
          ),
          child: Center(
            child: Icon(
              attachedToLeft
                  ? Icons.keyboard_double_arrow_right
                  : Icons.keyboard_double_arrow_left,
              size: 22,
              color: Colors.green.shade800,
            ),
          ),
        ),
      ),
    );
  }

  // ── Timeline panel ─────────────────────────────────────────────────────────

  Widget _buildTherapistTimelinePanel({bool inDrawer = false}) {
    final sessionId = _resolveTimelineSessionId();
    final hasSessionId = sessionId.isNotEmpty;
    final events = _visibleTimelineEventsForSession(sessionId);
    final templates = _therapistSessionSettings.timelineQuickNoteTemplates
        .map((entry) => entry.trim())
        .where((entry) => entry.isNotEmpty)
        .toList(growable: false);

    final timelineList = Builder(
      builder: (context) {
        if (_journalLoadError != null && _loadedJournalSessionId == sessionId) {
          return Center(
            child: Text(
              'Timeline unavailable: $_journalLoadError',
              style: TextStyle(
                color: Colors.red.shade700,
                fontSize: 12,
              ),
              textAlign: TextAlign.center,
            ),
          );
        }

        if (_journalLoading &&
            _loadedJournalSessionId == sessionId &&
            events.isEmpty) {
          return const Center(
            child: CircularProgressIndicator(),
          );
        }

        if (events.isEmpty) {
          return Center(
            child: Text(
              'No timeline events yet.',
              style: TextStyle(
                color: Colors.grey.shade700,
                fontSize: 12,
              ),
            ),
          );
        }

        return ListView.separated(
          itemCount: events.length,
          separatorBuilder: (_, __) => const SizedBox(height: 6),
          itemBuilder: (context, index) {
            return _buildTimelineEventTile(events[index]);
          },
        );
      },
    );

    final content = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            const Icon(Icons.timeline, size: 18),
            const SizedBox(width: 8),
            const Expanded(
              child: Text(
                'Session timeline',
                style: TextStyle(
                  fontWeight: FontWeight.w700,
                  fontSize: 13,
                ),
              ),
            ),
            if (hasSessionId)
              Text(
                'session: ${sessionId.length > 16 ? sessionId.substring(sessionId.length - 16) : sessionId}',
                style: TextStyle(
                  color: Colors.grey.shade700,
                  fontSize: 11,
                  fontWeight: FontWeight.w600,
                ),
              ),
          ],
        ),
        const SizedBox(height: 4),
        Text(
          'System events and therapist notes in one stream.',
          style: TextStyle(
            color: Colors.grey.shade700,
            fontSize: 12,
          ),
        ),
        const SizedBox(height: 10),
        if (!hasSessionId)
          _buildStateBanner(
            icon: Icons.sync,
            color: Colors.orange.shade800,
            text:
                'Timeline waits for session context. Reconnect/attach to start logging notes.',
          )
        else ...[
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _timelineNoteController,
                  enabled: !_timelineNoteInFlight,
                  textInputAction: TextInputAction.done,
                  onSubmitted: (value) {
                    if (_timelineNoteInFlight) {
                      return;
                    }
                    unawaited(
                      _appendTimelineNote(
                        noteText: value,
                        fromQuickTemplate: false,
                      ),
                    );
                  },
                  decoration: const InputDecoration(
                    isDense: true,
                    border: OutlineInputBorder(),
                    labelText: 'Add timeline note',
                    hintText: 'Type note and press Enter',
                  ),
                ),
              ),
              const SizedBox(width: 8),
              ElevatedButton.icon(
                onPressed: _timelineNoteInFlight
                    ? null
                    : () => unawaited(
                          _appendTimelineNote(
                            noteText: _timelineNoteController.text,
                            fromQuickTemplate: false,
                          ),
                        ),
                icon: _timelineNoteInFlight
                    ? const SizedBox(
                        width: 12,
                        height: 12,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(Icons.send, size: 16),
                label: const Text('Add'),
              ),
            ],
          ),
          const SizedBox(height: 8),
          if (templates.isNotEmpty)
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                for (final template in templates)
                  ActionChip(
                    label: Text(template),
                    onPressed: _timelineNoteInFlight
                        ? null
                        : () => unawaited(
                              _appendTimelineNote(
                                noteText: template,
                                fromQuickTemplate: true,
                              ),
                            ),
                  ),
              ],
            ),
          const SizedBox(height: 10),
          _buildVrGameRunsPanel(sessionId),
          if (inDrawer)
            Expanded(child: timelineList)
          else
            SizedBox(
              height: 250,
              child: timelineList,
            ),
        ],
      ],
    );

    if (inDrawer) {
      return Padding(
        padding: const EdgeInsets.all(12),
        child: content,
      );
    }

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey.shade100,
        borderRadius: BorderRadius.circular(8),
      ),
      child: content,
    );
  }

  Widget _buildContentStatusChip(PurchasedContentState state) {
    final label = switch (state.runtimeStatus) {
      ContentRuntimeStatus.notInstalled => 'Available',
      ContentRuntimeStatus.syncingManifest => 'Syncing manifest',
      ContentRuntimeStatus.downloading => 'Downloading',
      ContentRuntimeStatus.verifying => 'Verifying',
      ContentRuntimeStatus.activating => 'Activating',
      ContentRuntimeStatus.rollingBack => 'Rolling back',
      ContentRuntimeStatus.installing => 'Installing',
      ContentRuntimeStatus.ready => 'Installed',
      ContentRuntimeStatus.updateRequired => 'Update required',
      ContentRuntimeStatus.failed => 'Action needed',
    };
    final color = _contentStatusColor(state.runtimeStatus);

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: color.withValues(alpha: 0.35)),
      ),
      child: Text(
        label,
        style: TextStyle(
          color: color,
          fontSize: 10,
          fontWeight: FontWeight.w700,
        ),
      ),
    );
  }

  Widget _buildVersionChip(PurchasedContentState state) {
    final installedVersion = state.installedVersion ?? '-';
    final targetVersion =
        state.targetVersion.trim().isEmpty ? '-' : state.targetVersion.trim();
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: Colors.blueGrey.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: Colors.blueGrey.withValues(alpha: 0.2)),
      ),
      child: Text(
        'v$installedVersion -> v$targetVersion',
        style: const TextStyle(
          color: Colors.blueGrey,
          fontSize: 10,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }

  Color _contentStatusColor(ContentRuntimeStatus status) {
    switch (status) {
      case ContentRuntimeStatus.notInstalled:
        return Colors.grey.shade700;
      case ContentRuntimeStatus.syncingManifest:
        return Colors.blueGrey.shade700;
      case ContentRuntimeStatus.downloading:
        return Colors.blue.shade700;
      case ContentRuntimeStatus.verifying:
        return Colors.deepPurple.shade700;
      case ContentRuntimeStatus.activating:
        return Colors.teal.shade700;
      case ContentRuntimeStatus.rollingBack:
        return Colors.brown.shade700;
      case ContentRuntimeStatus.installing:
        return Colors.blue.shade700;
      case ContentRuntimeStatus.ready:
        return Colors.green.shade700;
      case ContentRuntimeStatus.updateRequired:
        return Colors.orange.shade700;
      case ContentRuntimeStatus.failed:
        return Colors.red.shade700;
    }
  }

  Future<void> _endSessionFromCatalog() async {
    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) => AlertDialog(
            title: const Text('End session?'),
            content: const Text(
              'This ends the current session and returns to student selection.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(false),
                child: const Text('Cancel'),
              ),
              ElevatedButton(
                onPressed: () => Navigator.of(context).pop(true),
                child: const Text('End Session'),
              ),
            ],
          ),
        ) ??
        false;

    if (!confirmed) {
      return;
    }

    await _endSessionAndReturnToStudentSelection();
  }

  Future<void> _terminateActiveSessionFromCatalog() async {
    final confirmed = await showDialog<bool>(
          context: context,
          builder: (context) => AlertDialog(
            title: const Text('Terminate active session?'),
            content: const Text(
              'Use this when runtime keeps stale session ownership/lock. '
              'The app will send END_SESSION and re-attach a new local session context.',
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.of(context).pop(false),
                child: const Text('Cancel'),
              ),
              ElevatedButton(
                onPressed: () => Navigator.of(context).pop(true),
                child: const Text('Terminate'),
              ),
            ],
          ),
        ) ??
        false;

    if (!confirmed) {
      return;
    }

    await _runPrimaryAction(() async {
      final ended = await _sendEndSessionWithConfirmation(
        reasonCode: 'END_SESSION_OVERRIDE',
        extraPayload: const <String, dynamic>{
          'reason': 'OwnershipConflictRecovery',
          'reasonCode': 'END_SESSION_OVERRIDE',
        },
      );
      if (!ended || !mounted) {
        return;
      }

      final newSessionId = _buildLocalSessionId();
      setState(() {
        _activeSessionId = newSessionId;
        _sessionAttachReady = false;
        _requiresSessionDecision = false;
        _remoteSessionIdPendingDecision = null;
      });

      await _ensureSessionAttached(
        reasonCode: 'END_SESSION_OVERRIDE',
        force: true,
        sessionIdOverride: newSessionId,
      );

      if (!mounted) {
        return;
      }

      if (_sessionAttachReady) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Session terminated and new context attached.',
            ),
            duration: Duration(seconds: 2),
          ),
        );
      } else {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Session terminated. Waiting for attach sync...',
            ),
            backgroundColor: Colors.orange,
            duration: Duration(seconds: 2),
          ),
        );
      }
    });
  }

  String _formatCompactDateTime(DateTime? valueUtc) {
    if (valueUtc == null) {
      return '--';
    }

    final local = valueUtc.toLocal();
    final day = local.day.toString().padLeft(2, '0');
    final month = local.month.toString().padLeft(2, '0');
    final hour = local.hour.toString().padLeft(2, '0');
    final minute = local.minute.toString().padLeft(2, '0');
    return '$day.$month $hour:$minute';
  }

  Widget _buildParentMetricChip({
    required String label,
    required String value,
    required IconData icon,
  }) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 6),
      decoration: BoxDecoration(
        color: Colors.blueGrey.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(
          color: Colors.blueGrey.withValues(alpha: 0.2),
        ),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            icon,
            size: 14,
            color: Colors.blueGrey.shade800,
          ),
          const SizedBox(width: 6),
          Text(
            '$label: $value',
            style: const TextStyle(
              color: Colors.blueGrey,
              fontSize: 11,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildParentQuickStartPanel() {
    final guidedStep = _resolveDefaultGuidedPlanStep();
    var guidedStepLaunchable = false;
    if (guidedStep != null) {
      final gameId = guidedStep.gameId.trim();
      for (final entry in _effectiveGameCatalog) {
        if (entry.gameId != gameId) {
          continue;
        }
        guidedStepLaunchable = entry.runtimeLaunchEnabled &&
            _isLaunchableContentState(_contentStateForGame(gameId));
        break;
      }
    }
    final canStartNow = _isConnected &&
        _sessionAttachReady &&
        !_isHeadsetPresenceBlocking &&
        !_isPlanBlockingLaunch &&
        guidedStepLaunchable &&
        !_isPrimaryActionInFlight;

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.green.shade50,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: Colors.green.shade200),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.family_restroom, color: Colors.green.shade800),
              const SizedBox(width: 8),
              Text(
                'Parent guided mode',
                style: TextStyle(
                  color: Colors.green.shade900,
                  fontWeight: FontWeight.w700,
                  fontSize: 13,
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            'Start one guided VR session and monitor live preview on mobile.',
            style: TextStyle(
              color: Colors.green.shade900,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            guidedStep == null
                ? 'No guided plan steps configured.'
                : 'Plan step: ${guidedStep.gameId} | '
                    'continuation: ${_therapistSessionSettings.guidedSessionContinuationPolicy.wireValue}',
            style: TextStyle(
              color: guidedStep == null
                  ? Colors.orange.shade900
                  : Colors.green.shade900,
              fontSize: 11,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 8),
          ElevatedButton.icon(
            onPressed: canStartNow
                ? () => unawaited(_startParentGuidedSession())
                : null,
            icon: const Icon(Icons.play_circle_fill),
            label: Text(
              canStartNow
                  ? 'Start guided session now'
                  : 'Waiting for connection/plan gate',
            ),
            style: ElevatedButton.styleFrom(
              backgroundColor: Colors.green.shade700,
              foregroundColor: Colors.white,
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildParentProgressPanel() {
    final snapshot = _parentProgressSnapshot;
    final completionPercent = (snapshot.completionRate * 100).toStringAsFixed(
      0,
    );

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.blueGrey.shade50,
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: Colors.blueGrey.shade100),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Row(
            children: [
              Icon(Icons.insights_outlined, size: 18),
              SizedBox(width: 8),
              Text(
                'Parent progress',
                style: TextStyle(
                  fontWeight: FontWeight.w700,
                  fontSize: 13,
                ),
              ),
            ],
          ),
          if (_parentInsightsLoading) ...[
            const SizedBox(height: 8),
            const LinearProgressIndicator(minHeight: 2),
          ],
          const SizedBox(height: 8),
          if (!_isProgressInsightsAllowed)
            Text(
              'Current plan does not include progress insights.',
              style: TextStyle(
                color: Colors.orange.shade800,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            )
          else if (!snapshot.hasData)
            Text(
              'No completed session history yet for this student.',
              style: TextStyle(
                color: Colors.grey.shade700,
                fontSize: 12,
              ),
            )
          else ...[
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                _buildParentMetricChip(
                  label: 'Sessions',
                  value: snapshot.totalSessions.toString(),
                  icon: Icons.history,
                ),
                _buildParentMetricChip(
                  label: 'Completed',
                  value: snapshot.terminalSessions.toString(),
                  icon: Icons.check_circle_outline,
                ),
                _buildParentMetricChip(
                  label: 'Completion',
                  value: '$completionPercent%',
                  icon: Icons.percent,
                ),
                _buildParentMetricChip(
                  label: 'Pending',
                  value: snapshot.unfinishedSessions.toString(),
                  icon: Icons.pending_actions_outlined,
                ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              'Last: ${snapshot.lastGameId.isEmpty ? '-' : snapshot.lastGameId} | '
              'state: ${snapshot.lastState.isEmpty ? '-' : snapshot.lastState} | '
              '${_formatCompactDateTime(snapshot.lastUpdatedAtUtc)}',
              style: TextStyle(
                color: Colors.grey.shade700,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
          const SizedBox(height: 10),
          Text(
            'Rewards',
            style: TextStyle(
              color: Colors.blueGrey.shade900,
              fontWeight: FontWeight.w700,
              fontSize: 12,
            ),
          ),
          const SizedBox(height: 4),
          if (!_isRewardsUnlocksAllowed)
            Text(
              'Current plan does not include VR reward unlocks.',
              style: TextStyle(
                color: Colors.orange.shade800,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            )
          else if (_recentRewardUnlocks.isEmpty)
            Text(
              'No unlocked rewards yet.',
              style: TextStyle(
                color: Colors.grey.shade700,
                fontSize: 12,
              ),
            )
          else
            Column(
              children: _recentRewardUnlocks.map((reward) {
                return Padding(
                  padding: const EdgeInsets.only(bottom: 6),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(
                        Icons.emoji_events_outlined,
                        size: 16,
                        color: Colors.amber.shade800,
                      ),
                      const SizedBox(width: 6),
                      Expanded(
                        child: Text(
                          '${reward.rewardTitle} (${reward.gameId}) | '
                          '${_formatCompactDateTime(reward.unlockedAtUtc)}',
                          style: TextStyle(
                            color: Colors.grey.shade800,
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                    ],
                  ),
                );
              }).toList(growable: false),
            ),
        ],
      ),
    );
  }

  Widget _buildSessionControlPanel({
    required _GameCatalogEntry entry,
    required PurchasedContentState contentState,
  }) {
    final controlsReady = _isControlLinkReadyForCommands;
    final planLaunchBlocked = _isPlanBlockingLaunch;
    final selectedGamePreparedOnHeadset = _isSelectedGamePreparedOnHeadset;
    final canStart = controlsReady &&
        !_isPrimaryActionInFlight &&
        !planLaunchBlocked &&
        entry.runtimeLaunchEnabled &&
        _isLaunchableContentState(contentState) &&
        !_isGameRuntimeActive &&
        selectedGamePreparedOnHeadset;
    final canRestart = controlsReady &&
        !_isPrimaryActionInFlight &&
        !planLaunchBlocked &&
        entry.runtimeLaunchEnabled &&
        _isLaunchableContentState(contentState) &&
        _isGameRuntimeActive;
    final canPause = controlsReady &&
        !_isPrimaryActionInFlight &&
        _isGameRuntimeActive &&
        !_isGameRuntimePaused;
    final canResume = controlsReady &&
        !_isPrimaryActionInFlight &&
        _isGameRuntimeActive &&
        _isGameRuntimePaused;
    final canEndGame =
        controlsReady && !_isPrimaryActionInFlight && _isGameRuntimeActive;
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text(
            'Game controls',
            style: TextStyle(
              fontWeight: FontWeight.w700,
              fontSize: 13,
            ),
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                child: ElevatedButton.icon(
                  onPressed:
                      canStart ? () => unawaited(_startFromSetup()) : null,
                  icon: const Icon(Icons.play_arrow),
                  label: const Text('Start'),
                  style:
                      ElevatedButton.styleFrom(backgroundColor: Colors.green),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: ElevatedButton.icon(
                  onPressed: canPause || canResume
                      ? () => unawaited(
                            _runPrimaryAction(() async {
                              await _sendCommand(
                                canResume
                                    ? CriticalCommandIds.resumeGame
                                    : CriticalCommandIds.pauseGame,
                              );
                            }),
                          )
                      : null,
                  icon: Icon(canResume ? Icons.play_circle : Icons.pause),
                  label: Text(canResume ? 'Resume' : 'Pause'),
                  style: ElevatedButton.styleFrom(
                    backgroundColor:
                        canResume ? Colors.teal : Colors.orange.shade700,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              Expanded(
                child: ElevatedButton.icon(
                  onPressed:
                      canRestart ? () => unawaited(_restartFromSetup()) : null,
                  icon: const Icon(Icons.restart_alt),
                  label: const Text('Restart'),
                  style:
                      ElevatedButton.styleFrom(backgroundColor: Colors.indigo),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          ElevatedButton.icon(
            onPressed:
                canEndGame ? () => unawaited(_stopRoundFromSetup()) : null,
            icon: const Icon(Icons.stop_circle_outlined),
            label: const Text('Stop'),
            style: ElevatedButton.styleFrom(
              minimumSize: const Size.fromHeight(44),
              backgroundColor: Colors.red.shade700,
            ),
          ),
          if (_planGateBannerText != null) ...[
            const SizedBox(height: 8),
            Text(
              _planGateBannerText!,
              style: TextStyle(
                color: Colors.orange.shade900,
                fontWeight: FontWeight.w600,
                fontSize: 12,
              ),
            ),
          ],
          if (!entry.runtimeLaunchEnabled) ...[
            const SizedBox(height: 8),
            Text(
              'Runtime launch for this game is disabled in current build.',
              style: TextStyle(
                color: Colors.orange.shade900,
                fontWeight: FontWeight.w600,
                fontSize: 12,
              ),
            ),
          ],
          if (!controlsReady) ...[
            const SizedBox(height: 8),
            Text(
              'Commands blocked: $_controlLinkBlockedHint',
              style: TextStyle(
                color: Colors.orange.shade900,
                fontWeight: FontWeight.w600,
                fontSize: 12,
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _buildGameCatalogStep() {
    final catalog = _entitledGameCatalog;
    return Container(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: <Color>[
            Colors.white,
            Colors.orange.shade50,
          ],
        ),
        borderRadius: BorderRadius.circular(24),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(10, 12, 10, 10),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (!_isConnected) ...[
              _buildStateBanner(
                icon: Icons.wifi_off,
                color: Colors.red.shade700,
                text: 'Headset is offline. Reconnect to continue.',
              ),
              const SizedBox(height: 10),
            ] else if (!_sessionAttachReady) ...[
              _buildStateBanner(
                icon: Icons.sync,
                color: Colors.orange.shade800,
                text: _sessionAttachInFlight
                    ? 'Synchronizing session context with headset...'
                    : 'Wait for headset session sync before starting a game.',
              ),
              const SizedBox(height: 10),
            ] else if (_headsetPresenceBannerText != null) ...[
              _buildStateBanner(
                icon: Icons.warning_amber_rounded,
                color: Colors.orange.shade800,
                text: _headsetPresenceBannerText!,
              ),
              const SizedBox(height: 10),
            ],
            if (_hasDeferredHandoff) ...[
              _buildDeferredHandoffBanner(),
              const SizedBox(height: 10),
            ],
            Expanded(
              child: catalog.isEmpty
                  ? Center(
                      child: Text(
                        'No bundled games available in the current build.',
                        style: TextStyle(color: Colors.grey[700]),
                      ),
                    )
                  : ListView(
                      padding: EdgeInsets.zero,
                      children: [
                        for (final entry in catalog)
                          _buildCatalogSessionCard(entry),
                        if (_isParentRole) ...[
                          const SizedBox(height: 12),
                          _buildParentQuickStartPanel(),
                          const SizedBox(height: 12),
                          _buildParentProgressPanel(),
                        ],
                      ],
                    ),
            ),
            const SizedBox(height: 12),
            if (_showCatalogRescueTerminateButton) ...[
              OutlinedButton.icon(
                onPressed: _isConnected && !_isPrimaryActionInFlight
                    ? () => unawaited(_terminateActiveSessionFromCatalog())
                    : null,
                icon: const Icon(Icons.power_settings_new),
                label: const Text('Terminate active session (rescue)'),
                style: OutlinedButton.styleFrom(
                  foregroundColor: Colors.deepOrange.shade700,
                  side: BorderSide(color: Colors.deepOrange.shade300),
                  padding: const EdgeInsets.symmetric(vertical: 12),
                ),
              ),
              const SizedBox(height: 8),
            ],
            ElevatedButton.icon(
              onPressed: _isPrimaryActionInFlight
                  ? null
                  : () => unawaited(_endSessionFromCatalog()),
              icon: const Icon(Icons.flag),
              label: const Text('End Session'),
              style: ElevatedButton.styleFrom(
                padding: const EdgeInsets.symmetric(vertical: 12),
                backgroundColor: Colors.deepOrange.shade700,
                foregroundColor: Colors.white,
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildCatalogSessionCard(_GameCatalogEntry entry) {
    final imageUrl = entry.thumbnailUrl.trim();
    final selected = _selectedGameId.trim() == entry.gameId;

    return Center(
      child: Padding(
        padding: const EdgeInsets.only(bottom: 16),
        child: GestureDetector(
          onTap: _isPrimaryActionInFlight
              ? null
              : () => unawaited(_openCatalogGameDetails(entry)),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 300),
            child: SizedBox(
              height: 200,
              child: Material(
                color: Colors.white,
                elevation: 8,
                shadowColor: Colors.black.withValues(alpha: 0.18),
                borderRadius: BorderRadius.circular(16),
                clipBehavior: Clip.antiAlias,
                child: Hero(
                  tag: _catalogHeroTag(entry.gameId),
                  child: Stack(
                    fit: StackFit.expand,
                    children: [
                      _buildCatalogArtworkBackdrop(entry),
                      if (imageUrl.isNotEmpty)
                        Image.network(
                          imageUrl,
                          fit: BoxFit.cover,
                          frameBuilder: (
                            context,
                            child,
                            frame,
                            wasSynchronouslyLoaded,
                          ) {
                            if (wasSynchronouslyLoaded) {
                              return child;
                            }

                            return AnimatedOpacity(
                              opacity: frame == null ? 0 : 1,
                              duration: const Duration(milliseconds: 220),
                              curve: Curves.easeOut,
                              child: child,
                            );
                          },
                          errorBuilder: (_, __, ___) => const SizedBox.shrink(),
                        ),
                      DecoratedBox(
                        decoration: BoxDecoration(
                          gradient: LinearGradient(
                            begin: Alignment.topCenter,
                            end: Alignment.bottomCenter,
                            colors: <Color>[
                              Colors.transparent,
                              Colors.black.withValues(alpha: 0.12),
                              Colors.black.withValues(alpha: 0.86),
                            ],
                          ),
                        ),
                      ),
                      if (selected)
                        Positioned(
                          top: 12,
                          right: 12,
                          child: Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 10,
                              vertical: 6,
                            ),
                            decoration: BoxDecoration(
                              color: Colors.white.withValues(alpha: 0.92),
                              borderRadius: BorderRadius.circular(999),
                            ),
                            child: Text(
                              'Current',
                              style: TextStyle(
                                color: Colors.grey.shade900,
                                fontSize: 11,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ),
                        ),
                      Positioned(
                        left: 16,
                        right: 16,
                        bottom: 14,
                        child: Text(
                          entry.title,
                          style: const TextStyle(
                            color: Colors.white,
                            fontSize: 22,
                            fontWeight: FontWeight.w800,
                          ),
                          maxLines: 2,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _openCatalogGameDetails(_GameCatalogEntry entry) async {
    final heroTag = _catalogHeroTag(entry.gameId);
    final contentState = _contentStateForGame(entry.gameId);
    final startBlockedReason = _catalogStartBlockedReason(entry, contentState);

    final shouldStart = await Navigator.of(context).push<bool>(
      MaterialPageRoute<bool>(
        fullscreenDialog: true,
        builder: (detailContext) {
          final canStart = startBlockedReason == null;
          final imageUrl = entry.thumbnailUrl.trim();
          final accent = _catalogAccentColor(entry.gameId);

          return Scaffold(
            backgroundColor: Colors.transparent,
            body: Container(
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: <Color>[
                    Colors.white,
                    Colors.orange.shade50,
                  ],
                ),
              ),
              child: SafeArea(
                child: SingleChildScrollView(
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 24),
                  child: Center(
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 440),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Container(
                            padding: const EdgeInsets.symmetric(vertical: 16),
                            decoration: BoxDecoration(
                              color: Colors.white,
                              borderRadius: BorderRadius.circular(24),
                              boxShadow: [
                                BoxShadow(
                                  color: Colors.black.withValues(alpha: 0.08),
                                  blurRadius: 20,
                                  offset: const Offset(0, 8),
                                ),
                              ],
                            ),
                            child: Center(
                              child: Text(
                                'Theraply Playground',
                                style: TextStyle(
                                  color: Colors.green.shade700,
                                  fontSize: 24,
                                  fontWeight: FontWeight.w800,
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(height: 18),
                          Material(
                            color: Colors.white,
                            elevation: 10,
                            shadowColor: Colors.black.withValues(alpha: 0.18),
                            borderRadius: BorderRadius.circular(24),
                            child: ClipRRect(
                              borderRadius: BorderRadius.circular(24),
                              child: AspectRatio(
                                aspectRatio: 3 / 2,
                                child: Stack(
                                  fit: StackFit.expand,
                                  children: [
                                    Hero(
                                      tag: heroTag,
                                      child: Stack(
                                        fit: StackFit.expand,
                                        children: [
                                          _buildCatalogArtworkBackdrop(entry),
                                          if (imageUrl.isNotEmpty)
                                            Image.network(
                                              imageUrl,
                                              fit: BoxFit.cover,
                                              frameBuilder: (
                                                context,
                                                child,
                                                frame,
                                                wasSynchronouslyLoaded,
                                              ) {
                                                if (wasSynchronouslyLoaded) {
                                                  return child;
                                                }

                                                return AnimatedOpacity(
                                                  opacity:
                                                      frame == null ? 0 : 1,
                                                  duration: const Duration(
                                                    milliseconds: 220,
                                                  ),
                                                  curve: Curves.easeOut,
                                                  child: child,
                                                );
                                              },
                                              errorBuilder: (_, __, ___) =>
                                                  const SizedBox.shrink(),
                                            ),
                                        ],
                                      ),
                                    ),
                                    DecoratedBox(
                                      decoration: BoxDecoration(
                                        gradient: LinearGradient(
                                          begin: Alignment.topCenter,
                                          end: Alignment.bottomCenter,
                                          colors: <Color>[
                                            Colors.black.withValues(
                                              alpha: 0.12,
                                            ),
                                            Colors.transparent,
                                            Colors.black.withValues(
                                              alpha: 0.38,
                                            ),
                                          ],
                                        ),
                                      ),
                                    ),
                                    Positioned(
                                      top: 14,
                                      left: 14,
                                      child: ElevatedButton.icon(
                                        onPressed: () {
                                          Navigator.of(detailContext).pop();
                                        },
                                        icon: const Icon(Icons.arrow_back),
                                        label: const Text('Back'),
                                        style: ElevatedButton.styleFrom(
                                          backgroundColor: Colors.white,
                                          foregroundColor: Colors.grey.shade900,
                                          elevation: 0,
                                          padding: const EdgeInsets.symmetric(
                                            horizontal: 14,
                                            vertical: 10,
                                          ),
                                          shape: RoundedRectangleBorder(
                                            borderRadius: BorderRadius.circular(
                                              999,
                                            ),
                                          ),
                                        ),
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(height: 18),
                          Container(
                            padding: const EdgeInsets.all(18),
                            decoration: BoxDecoration(
                              color: Colors.green.shade600,
                              borderRadius: BorderRadius.circular(24),
                              boxShadow: [
                                BoxShadow(
                                  color: Colors.green.shade900.withValues(
                                    alpha: 0.16,
                                  ),
                                  blurRadius: 18,
                                  offset: const Offset(0, 8),
                                ),
                              ],
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Row(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Container(
                                      padding: const EdgeInsets.all(10),
                                      decoration: BoxDecoration(
                                        color: Colors.white.withValues(
                                          alpha: 0.18,
                                        ),
                                        borderRadius: BorderRadius.circular(16),
                                      ),
                                      child: const Icon(
                                        Icons.play_circle_outline,
                                        color: Colors.white,
                                        size: 26,
                                      ),
                                    ),
                                    const SizedBox(width: 12),
                                    Expanded(
                                      child: Column(
                                        crossAxisAlignment:
                                            CrossAxisAlignment.start,
                                        children: [
                                          Text(
                                            entry.title,
                                            style: const TextStyle(
                                              color: Colors.white,
                                              fontSize: 24,
                                              fontWeight: FontWeight.w800,
                                            ),
                                          ),
                                          const SizedBox(height: 8),
                                          Text(
                                            entry.description,
                                            style: TextStyle(
                                              color: Colors.white.withValues(
                                                alpha: 0.94,
                                              ),
                                              fontSize: 14,
                                              height: 1.4,
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ],
                                ),
                                if (entry.previewLines.isNotEmpty) ...[
                                  const SizedBox(height: 16),
                                  for (final line in entry.previewLines.take(3))
                                    Padding(
                                      padding:
                                          const EdgeInsets.only(bottom: 10),
                                      child: Row(
                                        crossAxisAlignment:
                                            CrossAxisAlignment.start,
                                        children: [
                                          const Padding(
                                            padding: EdgeInsets.only(top: 2),
                                            child: Icon(
                                              Icons.auto_awesome,
                                              color: Colors.white,
                                              size: 16,
                                            ),
                                          ),
                                          const SizedBox(width: 10),
                                          Expanded(
                                            child: Text(
                                              line,
                                              style: TextStyle(
                                                color: Colors.white.withValues(
                                                  alpha: 0.92,
                                                ),
                                                fontSize: 13,
                                                height: 1.3,
                                              ),
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                ],
                              ],
                            ),
                          ),
                          const SizedBox(height: 18),
                          Container(
                            padding: const EdgeInsets.all(18),
                            decoration: BoxDecoration(
                              color: canStart
                                  ? accent.withValues(alpha: 0.92)
                                  : Colors.orange.shade700,
                              borderRadius: BorderRadius.circular(24),
                              boxShadow: [
                                BoxShadow(
                                  color: (canStart
                                          ? accent
                                          : Colors.orange.shade900)
                                      .withValues(alpha: 0.18),
                                  blurRadius: 18,
                                  offset: const Offset(0, 8),
                                ),
                              ],
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                Container(
                                  padding: const EdgeInsets.all(14),
                                  decoration: BoxDecoration(
                                    color: Colors.white,
                                    borderRadius: BorderRadius.circular(18),
                                  ),
                                  child: Row(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      Icon(
                                        canStart
                                            ? Icons.play_arrow_rounded
                                            : Icons.warning_amber_rounded,
                                        color: canStart
                                            ? accent
                                            : Colors.orange.shade800,
                                        size: 28,
                                      ),
                                      const SizedBox(width: 12),
                                      Expanded(
                                        child: Text(
                                          canStart
                                              ? 'Start will tell Unity to load this game and open the mobile setup screen.'
                                              : startBlockedReason,
                                          style: TextStyle(
                                            color: Colors.grey.shade900,
                                            fontSize: 14,
                                            height: 1.35,
                                            fontWeight: FontWeight.w600,
                                          ),
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                                const SizedBox(height: 16),
                                ElevatedButton.icon(
                                  onPressed: canStart
                                      ? () {
                                          Navigator.of(detailContext).pop(true);
                                        }
                                      : null,
                                  icon: Icon(
                                    canStart
                                        ? Icons.play_arrow
                                        : Icons.lock_outline,
                                  ),
                                  label: Text(
                                    canStart ? 'Start' : 'Start unavailable',
                                  ),
                                  style: ElevatedButton.styleFrom(
                                    backgroundColor: Colors.white,
                                    foregroundColor: canStart
                                        ? accent
                                        : Colors.grey.shade500,
                                    disabledBackgroundColor:
                                        Colors.white.withValues(alpha: 0.72),
                                    disabledForegroundColor:
                                        Colors.grey.shade500,
                                    minimumSize: const Size.fromHeight(54),
                                    textStyle: const TextStyle(
                                      fontSize: 18,
                                      fontWeight: FontWeight.w800,
                                    ),
                                    shape: RoundedRectangleBorder(
                                      borderRadius: BorderRadius.circular(18),
                                    ),
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
          );
        },
      ),
    );

    if (shouldStart != true || !mounted) {
      return;
    }

    final latestContentState = _contentStateForGame(entry.gameId);
    final latestBlockedReason = _catalogStartBlockedReason(
      entry,
      latestContentState,
    );
    if (latestBlockedReason != null) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(latestBlockedReason),
          backgroundColor: Colors.orange.shade800,
        ),
      );
      return;
    }

    setState(() {
      _selectedGameId = entry.gameId;
      _workflowStep = _WorkflowStep.gameSetup;
      _isVideoPreviewExpanded = true;
    });

    unawaited(
      _persistWorkflowCheckpoint(
        workflowStep: _WorkflowStep.gameSetup,
        reasonCode: 'SCREEN_GAME_SETUP',
        gameIdOverride: entry.gameId,
      ),
    );

    unawaited(
      _prepareSelectedGameScene(reasonCode: 'CATALOG_START_SELECTION'),
    );
  }

  String _catalogHeroTag(String gameId) {
    return 'catalog-game-$gameId';
  }

  String? _catalogStartBlockedReason(
    _GameCatalogEntry entry,
    PurchasedContentState contentState,
  ) {
    if (_isPrimaryActionInFlight) {
      return 'Please wait for the current action to finish.';
    }
    if (_requiresSessionDecision) {
      return 'Resolve the unfinished session decision before starting a new game.';
    }
    if (_isGameRuntimeActive) {
      return 'A game is already active. Return to the session screen to control it.';
    }
    if (!_isConnected) {
      return 'Headset is offline. Reconnect to continue.';
    }
    if (!_sessionAttachReady) {
      return _sessionAttachInFlight
          ? 'Session is still synchronizing with the headset.'
          : 'Wait for headset session sync before starting a game.';
    }
    if (_isHeadsetPresenceBlocking) {
      return _headsetPresenceBannerText ??
          'Headset is not inside the active VR app right now.';
    }
    if (_isPlanBlockingLaunch) {
      return _planGateBannerText ??
          'Current plan does not allow starting this session.';
    }
    if (!entry.runtimeLaunchEnabled) {
      return 'This game is not enabled for runtime launch in the current build.';
    }
    if (!_isLaunchableContentState(contentState)) {
      return _buildLaunchReadinessHint(entry, contentState);
    }
    return null;
  }

  Widget _buildGameSetupStep() {
    final entry = _selectedGameEntry;
    final contentState = _selectedContentState;
    final launchReadinessHint = _buildLaunchReadinessHint(entry, contentState);
    final setupLockedByRuntime = _isSetupLockedByRuntime;
    final selectedGamePreparationPending = _isSelectedGamePreparationPending;
    final setupLockedUntilSceneReady = !_isSelectedGameSceneReadyForControls;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _buildVideoPreviewPanel(
          subtitle: 'Live view while configuring and running this game.',
        ),
        const SizedBox(height: 8),
        Expanded(
          child: ListView(
            children: [
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: Colors.blueGrey[50],
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      entry.title,
                      style: const TextStyle(
                        fontSize: 15,
                        fontWeight: FontWeight.w700,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      entry.description,
                      style: TextStyle(color: Colors.grey[800]),
                    ),
                    const SizedBox(height: 6),
                    Wrap(
                      spacing: 6,
                      runSpacing: 6,
                      children: [
                        _buildContentStatusChip(contentState),
                        _buildVersionChip(contentState),
                      ],
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 8),
              if (!_isConnected) ...[
                _buildStateBanner(
                  icon: Icons.wifi_off,
                  color: Colors.red.shade700,
                  text:
                      'Headset is offline. Controls are disabled until reconnect.',
                ),
                const SizedBox(height: 8),
              ] else if (_isPreviewStreamExpected &&
                  _mediaPreviewState != MediaPreviewState.streaming) ...[
                _buildStateBanner(
                  icon: Icons.wifi_tethering_error_rounded,
                  color: Colors.orange.shade800,
                  text:
                      'VR preview is unavailable. Commands remain available if session sync is ready.',
                ),
                const SizedBox(height: 8),
              ] else if (_headsetPresenceBannerText != null) ...[
                _buildStateBanner(
                  icon: Icons.warning_amber_rounded,
                  color: Colors.orange.shade800,
                  text: _headsetPresenceBannerText!,
                ),
                const SizedBox(height: 8),
              ] else if (!_sessionAttachReady) ...[
                _buildStateBanner(
                  icon: Icons.sync,
                  color: Colors.orange.shade800,
                  text: _sessionAttachInFlight
                      ? 'Synchronizing session context...'
                      : 'Session context is not synced yet. Controls remain locked.',
                ),
                const SizedBox(height: 8),
              ],
              if (_hasDeferredHandoff) ...[
                _buildDeferredHandoffBanner(),
                const SizedBox(height: 8),
              ],
              if (!_isLaunchableContentState(contentState)) ...[
                _buildStateBanner(
                  icon: Icons.warning_amber_rounded,
                  color: Colors.orange.shade800,
                  text: launchReadinessHint,
                ),
                const SizedBox(height: 8),
              ],
              if (selectedGamePreparationPending) ...[
                _buildStateBanner(
                  icon: Icons.hourglass_top_rounded,
                  color: Colors.blueGrey.shade700,
                  text: _selectedGameSceneLoadingHint,
                ),
                const SizedBox(height: 8),
              ],
              if (setupLockedByRuntime) ...[
                _buildStateBanner(
                  icon: Icons.lock,
                  color: Colors.orange.shade800,
                  text:
                      'Settings are locked while a game is active. Use controls below.',
                ),
                const SizedBox(height: 8),
              ],
              if (entry.mobileControlSchema == null &&
                  entry.mobileControlSchemaReasonCode.trim().isNotEmpty) ...[
                _buildStateBanner(
                  icon: Icons.rule_folder_outlined,
                  color: Colors.orange.shade800,
                  text:
                      'Invalid mobile controls contract (${entry.mobileControlSchemaReasonCode}). Falling back to static setup.',
                ),
                const SizedBox(height: 8),
              ],
              if (entry.mobileControlSchema != null)
                _buildSchemaDrivenSettings(
                  entry.mobileControlSchema!,
                  lockedByRuntime: setupLockedByRuntime,
                  lockedUntilSceneReady: setupLockedUntilSceneReady,
                )
              else ...[
                if (_isDemoCubeGameSelected)
                  _buildDemoCubeSettings(
                    lockedByRuntime: setupLockedByRuntime,
                    lockedUntilSceneReady: setupLockedUntilSceneReady,
                  ),
                if (_isPulseTargetGameSelected)
                  _buildPulseTargetsSettings(
                    lockedByRuntime: setupLockedByRuntime,
                    lockedUntilSceneReady: setupLockedUntilSceneReady,
                  ),
                if (!_isDemoCubeGameSelected && !_isPulseTargetGameSelected)
                  _buildGenericGameSettings(
                    entry,
                    lockedByRuntime: setupLockedByRuntime,
                    lockedUntilSceneReady: setupLockedUntilSceneReady,
                  ),
              ],
              const SizedBox(height: 8),
              _buildSessionControlPanel(
                entry: entry,
                contentState: contentState,
              ),
              const SizedBox(height: 8),
              if (_isParentRole) _buildParentProgressPanel(),
            ],
          ),
        ),
      ],
    );
  }

  Widget _buildCatalogArtworkBackdrop(_GameCatalogEntry entry) {
    final colors = _catalogArtworkGradient(entry.gameId);
    final icon = _catalogArtworkIcon(entry.gameId);

    return Container(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: colors,
        ),
      ),
      child: Stack(
        children: [
          Positioned(
            right: -26,
            top: -18,
            child: Container(
              width: 120,
              height: 120,
              decoration: BoxDecoration(
                color: Colors.white.withValues(alpha: 0.12),
                shape: BoxShape.circle,
              ),
            ),
          ),
          Positioned(
            left: -18,
            bottom: -26,
            child: Container(
              width: 92,
              height: 92,
              decoration: BoxDecoration(
                color: Colors.white.withValues(alpha: 0.10),
                shape: BoxShape.circle,
              ),
            ),
          ),
          Align(
            alignment: Alignment.center,
            child: Icon(
              icon,
              size: 72,
              color: Colors.white.withValues(alpha: 0.34),
            ),
          ),
        ],
      ),
    );
  }

  List<Color> _catalogArtworkGradient(String gameId) {
    switch (gameId.trim().toLowerCase()) {
      case 'piniata':
        return const <Color>[Color(0xFFFF8A00), Color(0xFFFF4D6D)];
      case 'butterflies':
        return const <Color>[Color(0xFF00B3A6), Color(0xFF6DD400)];
      case 'coding':
        return const <Color>[Color(0xFF2F6BFF), Color(0xFF8A5CFF)];
      case 'hiding_game':
        return const <Color>[Color(0xFF0F766E), Color(0xFF0EA5E9)];
      case 'passive_mindfulness':
        return const <Color>[Color(0xFF4CB8C4), Color(0xFF3CD3AD)];
      case 'active_mindfulness':
        return const <Color>[Color(0xFFFF7A18), Color(0xFFFFB347)];
      case 'puzzle':
        return const <Color>[Color(0xFFF97316), Color(0xFFFACC15)];
      case 'both_hands':
        return const <Color>[Color(0xFF2563EB), Color(0xFF14B8A6)];
      case 'spatial':
        return const <Color>[Color(0xFF0F766E), Color(0xFF84CC16)];
      case 'christmas':
        return const <Color>[Color(0xFFB91C1C), Color(0xFF15803D)];
      default:
        return const <Color>[Color(0xFF64748B), Color(0xFFCBD5E1)];
    }
  }

  IconData _catalogArtworkIcon(String gameId) {
    switch (gameId.trim().toLowerCase()) {
      case 'piniata':
        return Icons.celebration;
      case 'butterflies':
        return Icons.filter_vintage;
      case 'coding':
        return Icons.music_note_rounded;
      case 'hiding_game':
        return Icons.hearing;
      case 'passive_mindfulness':
        return Icons.self_improvement;
      case 'active_mindfulness':
        return Icons.auto_awesome;
      case 'puzzle':
        return Icons.extension_rounded;
      case 'both_hands':
        return Icons.back_hand_outlined;
      case 'spatial':
        return Icons.grid_on_rounded;
      case 'christmas':
        return Icons.stars_rounded;
      default:
        return Icons.videogame_asset_rounded;
    }
  }

  Color _catalogAccentColor(String gameId) {
    return _catalogArtworkGradient(gameId).first;
  }

  Widget _buildSchemaDrivenSettings(
    MobileControlSchema schema, {
    required bool lockedByRuntime,
    required bool lockedUntilSceneReady,
  }) {
    _ensureDynamicControlValuesForSelectedSchema();
    final settingsLocked = lockedByRuntime || lockedUntilSceneReady;

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (lockedByRuntime)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Text(
                'Runtime active: value controls are locked. Schema buttons remain available.',
                style: TextStyle(
                  color: Colors.orange[800],
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          MobileControlRenderer(
            schema: schema,
            valuesByControlId: _dynamicControlValuesByControlId,
            locked: settingsLocked,
            lockButtons: lockedUntilSceneReady,
            onValueChanged: (change) {
              setState(() {
                _dynamicControlValuesByControlId[change.control.controlId] =
                    change.value;
              });
            },
            onButtonPressed: (control) {
              unawaited(_handleSchemaButtonControl(control));
            },
          ),
        ],
      ),
    );
  }

  Widget _buildDemoCubeSettings({
    required bool lockedByRuntime,
    required bool lockedUntilSceneReady,
  }) {
    final settingsLocked = lockedByRuntime || lockedUntilSceneReady;

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Ustawienia gry',
            style: TextStyle(
              color: Colors.grey[800],
              fontSize: 12,
              fontWeight: FontWeight.w700,
            ),
          ),
          if (lockedByRuntime)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Text(
                'Ustawienia zablokowane podczas aktywnej gry. Dostepne akcje: Pause, Stop, Restart, End Session.',
                style: TextStyle(
                  color: Colors.orange[800],
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          const SizedBox(height: 8),
          Text(
            'Ilosc cubey: $_demoCubeCount',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Slider(
            value: _demoCubeCount.toDouble(),
            min: 4,
            max: 40,
            divisions: 36,
            label: _demoCubeCount.toString(),
            onChanged: settingsLocked
                ? null
                : (value) {
                    setState(() {
                      _demoCubeCount = value.round();
                    });
                  },
          ),
          Text(
            'Predkosc: ${_demoCubeSpeed.toStringAsFixed(2)}',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Slider(
            value: _demoCubeSpeed,
            min: 0.2,
            max: 2.2,
            divisions: 20,
            label: _demoCubeSpeed.toStringAsFixed(2),
            onChanged: settingsLocked
                ? null
                : (value) {
                    setState(() {
                      _demoCubeSpeed = value;
                    });
                  },
          ),
          DropdownButtonFormField<String>(
            initialValue: _demoLevelMode,
            decoration: InputDecoration(
              labelText: 'Poziom',
              border: OutlineInputBorder(
                borderRadius: BorderRadius.circular(6),
              ),
              contentPadding: const EdgeInsets.symmetric(
                horizontal: 8,
                vertical: 8,
              ),
            ),
            items: _demoLevelModes
                .map(
                  (mode) => DropdownMenuItem<String>(
                    value: mode,
                    child: Text(mode),
                  ),
                )
                .toList(),
            onChanged: settingsLocked
                ? null
                : (value) {
                    if (value == null) {
                      return;
                    }

                    setState(() {
                      _demoLevelMode = value;
                    });
                  },
          ),
        ],
      ),
    );
  }

  Widget _buildGenericGameSettings(
    _GameCatalogEntry entry, {
    required bool lockedByRuntime,
    required bool lockedUntilSceneReady,
  }) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Text(
        lockedByRuntime
            ? 'Gra aktywna: zmiana konfiguracji jest chwilowo zablokowana. Zatrzymaj lub zakoncz sesje, aby edytowac ustawienia.'
            : 'Gra `${entry.title}` nie ma dedykowanego panelu ustawien po stronie mobilki. '
                'Uzywamy domyslnej konfiguracji runtime.',
        style: TextStyle(color: Colors.grey[700], fontSize: 12),
      ),
    );
  }

  Widget _buildPulseTargetsSettings({
    required bool lockedByRuntime,
    required bool lockedUntilSceneReady,
  }) {
    final settingsLocked = lockedByRuntime || lockedUntilSceneReady;

    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: Colors.grey[100],
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Ustawienia gry',
            style: TextStyle(
              color: Colors.grey[800],
              fontSize: 12,
              fontWeight: FontWeight.w700,
            ),
          ),
          if (lockedByRuntime)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                'Ustawienia zablokowane podczas aktywnej gry. Dostepne akcje: Pause, Stop, Restart, End Session.',
                style: TextStyle(
                  color: Colors.orange[800],
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          const SizedBox(height: 8),
          Text(
            'Ilosc celow: $_pulseTargetCount',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Slider(
            value: _pulseTargetCount.toDouble(),
            min: 3,
            max: 32,
            divisions: 29,
            label: _pulseTargetCount.toString(),
            onChanged: settingsLocked
                ? null
                : (value) {
                    setState(() {
                      _pulseTargetCount = value.round();
                    });
                  },
          ),
          Text(
            'Predkosc celu: ${_pulseTargetSpeed.toStringAsFixed(2)}',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Slider(
            value: _pulseTargetSpeed,
            min: 0.2,
            max: 2.2,
            divisions: 20,
            label: _pulseTargetSpeed.toStringAsFixed(2),
            onChanged: settingsLocked
                ? null
                : (value) {
                    setState(() {
                      _pulseTargetSpeed = value;
                    });
                  },
          ),
          Text(
            'Skala celu: ${_pulseTargetScale.toStringAsFixed(2)}',
            style: TextStyle(color: Colors.grey[700], fontSize: 12),
          ),
          Slider(
            value: _pulseTargetScale,
            min: 0.12,
            max: 1.0,
            divisions: 22,
            label: _pulseTargetScale.toStringAsFixed(2),
            onChanged: settingsLocked
                ? null
                : (value) {
                    setState(() {
                      _pulseTargetScale = value;
                    });
                  },
          ),
        ],
      ),
    );
  }
}

class _AppliedGameConfigSnapshot {
  final String gameConfigType;
  final int gameConfigVersion;
  final String gameConfigJson;

  const _AppliedGameConfigSnapshot({
    required this.gameConfigType,
    required this.gameConfigVersion,
    required this.gameConfigJson,
  });
}

class _GameCatalogEntry {
  final String gameId;
  final String title;
  final String description;
  final String targetContentVersion;
  final String packageUri;
  final String thumbnailUrl;
  final bool supportsSaveResume;
  final bool availableForPurchase;
  final bool requiresExplicitLicense;
  final bool runtimeLaunchEnabled;
  final int sortOrder;
  final List<String> previewLines;
  final MobileControlSchema? mobileControlSchema;
  final String mobileControlSchemaReasonCode;

  const _GameCatalogEntry({
    required this.gameId,
    required this.title,
    required this.description,
    required this.targetContentVersion,
    required this.packageUri,
    required this.thumbnailUrl,
    required this.supportsSaveResume,
    required this.availableForPurchase,
    required this.requiresExplicitLicense,
    required this.runtimeLaunchEnabled,
    required this.sortOrder,
    required this.previewLines,
    this.mobileControlSchema,
    this.mobileControlSchemaReasonCode = '',
  });
}
