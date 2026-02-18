class AdminAuditEvent {
  final String actorUid;
  final String? actorEmail;
  final String actorRole;
  final String action;
  final String targetCollection;
  final String targetDocumentId;
  final String? targetUserId;
  final DateTime occurredAtUtc;
  final String reason;
  final String correlationId;
  final Map<String, dynamic> payloadSummary;

  const AdminAuditEvent({
    required this.actorUid,
    required this.actorEmail,
    required this.actorRole,
    required this.action,
    required this.targetCollection,
    required this.targetDocumentId,
    required this.targetUserId,
    required this.occurredAtUtc,
    required this.reason,
    required this.correlationId,
    required this.payloadSummary,
  });

  Map<String, dynamic> toFirestore() {
    return <String, dynamic>{
      'actorUid': actorUid,
      'actorEmail': actorEmail,
      'actorRole': actorRole,
      'action': action,
      'targetCollection': targetCollection,
      'targetDocumentId': targetDocumentId,
      'targetUserId': targetUserId,
      'occurredAtUtc': occurredAtUtc.toUtc().toIso8601String(),
      'reason': reason,
      'correlationId': correlationId,
      'payloadSummary': payloadSummary,
    };
  }
}
