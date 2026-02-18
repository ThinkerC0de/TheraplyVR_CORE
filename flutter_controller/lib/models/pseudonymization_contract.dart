enum PseudonymizationMode {
  standard,
  clinicalResearch,
}

extension PseudonymizationModeCodec on PseudonymizationMode {
  static PseudonymizationMode fromWire(String? wireValue) {
    switch (wireValue) {
      case 'CLINICAL_RESEARCH':
        return PseudonymizationMode.clinicalResearch;
      case 'STANDARD':
      default:
        return PseudonymizationMode.standard;
    }
  }

  String get wireValue {
    switch (this) {
      case PseudonymizationMode.standard:
        return 'STANDARD';
      case PseudonymizationMode.clinicalResearch:
        return 'CLINICAL_RESEARCH';
    }
  }
}

class IdentityBindingPayload {
  final String schemaVersion;
  final String tenantId;
  final String therapistId;
  final String studentId;
  final String therapistPseudoId;
  final String studentPseudoId;
  final String pseudonymKeyRef;
  final String algorithmTag;
  final DateTime createdAtUtc;
  final DateTime? expiresAtUtc;

  const IdentityBindingPayload({
    required this.schemaVersion,
    required this.tenantId,
    required this.therapistId,
    required this.studentId,
    required this.therapistPseudoId,
    required this.studentPseudoId,
    required this.pseudonymKeyRef,
    required this.algorithmTag,
    required this.createdAtUtc,
    this.expiresAtUtc,
  });

  factory IdentityBindingPayload.fromJson(Map<String, dynamic> json) {
    return IdentityBindingPayload(
      schemaVersion: json['schemaVersion'] as String? ?? '2026-02-18',
      tenantId: json['tenantId'] as String? ?? '',
      therapistId: json['therapistId'] as String? ?? '',
      studentId: json['studentId'] as String? ?? '',
      therapistPseudoId: json['therapistPseudoId'] as String? ?? '',
      studentPseudoId: json['studentPseudoId'] as String? ?? '',
      pseudonymKeyRef: json['pseudonymKeyRef'] as String? ?? '',
      algorithmTag: json['algorithmTag'] as String? ?? '',
      createdAtUtc: DateTime.tryParse(json['createdAtUtc'] as String? ?? '')
              ?.toUtc() ??
          DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
      expiresAtUtc: DateTime.tryParse(json['expiresAtUtc'] as String? ?? '')
          ?.toUtc(),
    );
  }

  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'schemaVersion': schemaVersion,
      'tenantId': tenantId,
      'therapistId': therapistId,
      'studentId': studentId,
      'therapistPseudoId': therapistPseudoId,
      'studentPseudoId': studentPseudoId,
      'pseudonymKeyRef': pseudonymKeyRef,
      'algorithmTag': algorithmTag,
      'createdAtUtc': createdAtUtc.toUtc().toIso8601String(),
      'expiresAtUtc': expiresAtUtc?.toUtc().toIso8601String(),
    };
  }
}

class SessionTelemetryPayload {
  final String schemaVersion;
  final PseudonymizationMode mode;
  final String tenantId;
  final String sessionPseudoId;
  final String therapistPseudoId;
  final String studentPseudoId;
  final String gameId;
  final DateTime eventAtUtc;
  final String eventType;
  final Map<String, dynamic> metrics;
  final String pseudonymKeyRef;
  final String algorithmTag;

  const SessionTelemetryPayload({
    required this.schemaVersion,
    required this.mode,
    required this.tenantId,
    required this.sessionPseudoId,
    required this.therapistPseudoId,
    required this.studentPseudoId,
    required this.gameId,
    required this.eventAtUtc,
    required this.eventType,
    required this.metrics,
    required this.pseudonymKeyRef,
    required this.algorithmTag,
  });

  factory SessionTelemetryPayload.fromJson(Map<String, dynamic> json) {
    return SessionTelemetryPayload(
      schemaVersion: json['schemaVersion'] as String? ?? '2026-02-18',
      mode: PseudonymizationModeCodec.fromWire(json['mode'] as String?),
      tenantId: json['tenantId'] as String? ?? '',
      sessionPseudoId: json['sessionPseudoId'] as String? ?? '',
      therapistPseudoId: json['therapistPseudoId'] as String? ?? '',
      studentPseudoId: json['studentPseudoId'] as String? ?? '',
      gameId: json['gameId'] as String? ?? '',
      eventAtUtc: DateTime.tryParse(json['eventAtUtc'] as String? ?? '')
              ?.toUtc() ??
          DateTime.fromMillisecondsSinceEpoch(0, isUtc: true),
      eventType: json['eventType'] as String? ?? '',
      metrics: json['metrics'] is Map<String, dynamic>
          ? Map<String, dynamic>.from(json['metrics'] as Map<String, dynamic>)
          : <String, dynamic>{},
      pseudonymKeyRef: json['pseudonymKeyRef'] as String? ?? '',
      algorithmTag: json['algorithmTag'] as String? ?? '',
    );
  }

  Map<String, dynamic> toJson() {
    return <String, dynamic>{
      'schemaVersion': schemaVersion,
      'mode': mode.wireValue,
      'tenantId': tenantId,
      'sessionPseudoId': sessionPseudoId,
      'therapistPseudoId': therapistPseudoId,
      'studentPseudoId': studentPseudoId,
      'gameId': gameId,
      'eventAtUtc': eventAtUtc.toUtc().toIso8601String(),
      'eventType': eventType,
      'metrics': metrics,
      'pseudonymKeyRef': pseudonymKeyRef,
      'algorithmTag': algorithmTag,
    };
  }

  bool get containsDirectIdentifiers {
    final payloadAsText = toJson().toString().toLowerCase();
    return payloadAsText.contains('firstname') ||
        payloadAsText.contains('lastname') ||
        payloadAsText.contains('email');
  }
}
