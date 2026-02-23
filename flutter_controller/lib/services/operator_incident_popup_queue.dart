import 'dart:async';
import 'dart:collection';

import 'package:flutter/material.dart';
import 'package:flutter_controller/models/ops_error_catalog.dart';
import 'package:flutter_controller/models/therapist_session_settings.dart';
import 'package:flutter_controller/services/operator_incident_report_service.dart';
import 'package:url_launcher/url_launcher.dart';

enum OperatorIncidentSeverity { error, warning, info }

@immutable
class OperatorIncidentAlert {
  final DateTime occurredAtUtc;
  final String source;
  final String title;
  final String message;
  final String reasonCode;
  final OperatorIncidentSeverity severity;
  final Map<String, dynamic> contextData;

  const OperatorIncidentAlert({
    required this.occurredAtUtc,
    required this.source,
    required this.title,
    required this.message,
    required this.reasonCode,
    required this.severity,
    this.contextData = const <String, dynamic>{},
  });

  String get reasonTag => OpsErrorCatalog.buildReasonTag(reasonCode);
}

String buildOperatorIncidentHistoryReport(
  Iterable<OperatorIncidentAlert> alerts, {
  TherapistUiLanguage language = TherapistUiLanguage.english,
}) {
  final strings = _stringsFor(language);
  final buffer = StringBuffer();
  var index = 1;
  for (final alert in alerts) {
    final timestamp = alert.occurredAtUtc.toLocal().toIso8601String();
    final severity = alert.severity.name.toUpperCase();
    buffer.writeln(
      '$index. [$timestamp] ${alert.reasonTag} $severity ${alert.source} ${alert.title}',
    );
    buffer.writeln('   ${alert.message}');
    index++;
  }

  if (index == 1) {
    return strings.noIncidentAlertsCaptured;
  }

  return buffer.toString().trimRight();
}

@immutable
class OperatorIncidentEmailDraft {
  final String subject;
  final String body;

  const OperatorIncidentEmailDraft({
    required this.subject,
    required this.body,
  });
}

OperatorIncidentEmailDraft buildOperatorIncidentEmailDraft({
  required String queueName,
  required String reportId,
  required DateTime generatedAtUtc,
  required Iterable<OperatorIncidentAlert> alerts,
  TherapistUiLanguage language = TherapistUiLanguage.english,
}) {
  final strings = _stringsFor(language);
  final normalizedQueueName =
      queueName.trim().isEmpty ? 'unknown_queue' : queueName.trim();
  final generatedAtLabel = generatedAtUtc.toUtc().toIso8601String();
  final history = buildOperatorIncidentHistoryReport(
    alerts,
    language: language,
  );
  final alertCount = alerts.length;
  final headline = alertCount > 0
      ? '${strings.incidentsLabel}: $alertCount'
      : '${strings.incidentsLabel}: ${strings.noneLabel}';
  final subject =
      '[Theraply][Incident] $normalizedQueueName | report=$reportId';
  final body = StringBuffer()
    ..writeln(strings.mailTitle)
    ..writeln('Report ID: $reportId')
    ..writeln('Queue: $normalizedQueueName')
    ..writeln('Generated UTC: $generatedAtLabel')
    ..writeln(headline)
    ..writeln()
    ..writeln(strings.eventSequenceLabel)
    ..writeln(history);

  return OperatorIncidentEmailDraft(
    subject: subject,
    body: body.toString().trimRight(),
  );
}

class OperatorIncidentPopupQueue {
  static const String _incidentReportInboxEmail = 'errors@pranasense.pl';
  static const int _mailBodyMaxChars = 7000;

  final String queueName;
  final TherapistUiLanguage Function()? languageResolver;

  final ListQueue<OperatorIncidentAlert> _pending =
      ListQueue<OperatorIncidentAlert>();
  final List<OperatorIncidentAlert> _history = <OperatorIncidentAlert>[];
  bool _isPresenting = false;

  OperatorIncidentPopupQueue({
    required this.queueName,
    this.languageResolver,
  });

  int get pendingCount => _pending.length;
  int get historyCount => _history.length;
  TherapistUiLanguage get _language =>
      languageResolver?.call() ?? TherapistUiLanguage.english;

  void enqueue(BuildContext context, OperatorIncidentAlert alert) {
    _pending.addLast(alert);
    _history.add(alert);
    if (_isPresenting) {
      return;
    }

    unawaited(_drain(context));
  }

  Future<void> _drain(BuildContext context) async {
    if (_isPresenting || !context.mounted) {
      return;
    }

    _isPresenting = true;
    try {
      while (context.mounted && _pending.isNotEmpty) {
        final alert = _pending.removeFirst();
        final pendingAfter = _pending.length;
        await _showDialogForAlert(
          context: context,
          alert: alert,
          pendingAfter: pendingAfter,
        );
      }
    } finally {
      _isPresenting = false;
    }
  }

  Future<String> _submitHistoryReport() async {
    final snapshot = List<OperatorIncidentAlert>.unmodifiable(_history);
    final language = _language;
    final generatedAtUtc = DateTime.now().toUtc();
    final reportContext = <String, dynamic>{
      'queueName': queueName,
      'capturedAlertCount': snapshot.length,
      'operatorUiLanguage': language.wireValue,
    };
    if (snapshot.isNotEmpty) {
      reportContext['firstOccurredAtUtc'] =
          snapshot.first.occurredAtUtc.toIso8601String();
      reportContext['lastOccurredAtUtc'] =
          snapshot.last.occurredAtUtc.toIso8601String();
    }

    return OperatorIncidentReportService.submitIncidentReport(
      queueName: queueName,
      generatedAtUtc: generatedAtUtc,
      historyReport: buildOperatorIncidentHistoryReport(
        snapshot,
        language: language,
      ),
      incidents: _buildIncidentPayload(snapshot),
      context: reportContext,
    );
  }

  Future<String> _submitReportAndOpenEmailDraft() async {
    final snapshot = List<OperatorIncidentAlert>.unmodifiable(_history);
    final language = _language;
    final strings = _stringsFor(language);
    final generatedAtUtc = DateTime.now().toUtc();
    final reportId = await _submitHistoryReport();
    final emailDraft = buildOperatorIncidentEmailDraft(
      queueName: queueName,
      reportId: reportId,
      generatedAtUtc: generatedAtUtc,
      alerts: snapshot,
      language: language,
    );
    final truncatedBody = _truncateMailBody(
      emailDraft.body,
      language: language,
    );
    final mailtoUri = Uri(
      scheme: 'mailto',
      path: _incidentReportInboxEmail,
      queryParameters: <String, String>{
        'subject': emailDraft.subject,
        'body': truncatedBody,
      },
    );
    final launched = await launchUrl(
      mailtoUri,
      mode: LaunchMode.externalApplication,
    );
    if (!launched) {
      throw StateError(
        strings.cannotOpenEmailClient(
          inboxEmail: _incidentReportInboxEmail,
          reportId: reportId,
        ),
      );
    }
    return reportId;
  }

  static String _truncateMailBody(
    String input, {
    required TherapistUiLanguage language,
  }) {
    if (input.length <= _mailBodyMaxChars) {
      return input;
    }
    final suffix = _stringsFor(language).truncatedSuffix;
    final keepLength = _mailBodyMaxChars - suffix.length;
    if (keepLength <= 0) {
      return suffix.trimLeft();
    }
    return '${input.substring(0, keepLength)}$suffix';
  }

  static List<Map<String, dynamic>> _buildIncidentPayload(
    List<OperatorIncidentAlert> alerts,
  ) {
    final payload = <Map<String, dynamic>>[];
    for (var index = 0; index < alerts.length; index++) {
      final alert = alerts[index];
      payload.add(<String, dynamic>{
        'sequence': index + 1,
        'occurredAtUtc': alert.occurredAtUtc.toIso8601String(),
        'occurredAtUnixMs': alert.occurredAtUtc.millisecondsSinceEpoch,
        'source': alert.source,
        'title': alert.title,
        'message': alert.message,
        'reasonCode': alert.reasonCode,
        'reasonTag': alert.reasonTag,
        'severity': alert.severity.name,
        'context': _normalizeContextData(alert.contextData),
      });
    }
    return payload;
  }

  static Map<String, dynamic> _normalizeContextData(
      Map<String, dynamic> input) {
    final normalized = <String, dynamic>{};
    input.forEach((key, value) {
      normalized[key] = _normalizeContextValue(value);
    });
    return normalized;
  }

  static dynamic _normalizeContextValue(dynamic value) {
    if (value == null || value is num || value is bool || value is String) {
      return value;
    }

    if (value is DateTime) {
      return value.toUtc().toIso8601String();
    }

    if (value is Enum) {
      return value.name;
    }

    if (value is Map) {
      final nested = <String, dynamic>{};
      value.forEach((key, nestedValue) {
        nested[key.toString()] = _normalizeContextValue(nestedValue);
      });
      return nested;
    }

    if (value is Iterable) {
      return value.map(_normalizeContextValue).toList(growable: false);
    }

    return value.toString();
  }

  Future<void> _showDialogForAlert({
    required BuildContext context,
    required OperatorIncidentAlert alert,
    required int pendingAfter,
  }) async {
    if (!context.mounted) {
      return;
    }

    final severityColor = _resolveSeverityColor(alert.severity);
    final severityIcon = _resolveSeverityIcon(alert.severity);
    final strings = _stringsFor(_language);
    final severityLabel = alert.severity.name.toUpperCase();
    final occurredLabel = alert.occurredAtUtc.toLocal().toIso8601String();
    final hasPending = pendingAfter > 0;

    await showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (dialogContext) {
        var isSubmittingReport = false;
        String? reportSubmitStatus;
        var reportSubmitFailed = false;

        return StatefulBuilder(
          builder: (dialogContext, setDialogState) {
            Future<void> handleSubmitReport() async {
              if (isSubmittingReport) {
                return;
              }

              setDialogState(() {
                isSubmittingReport = true;
                reportSubmitStatus = null;
                reportSubmitFailed = false;
              });

              try {
                final reportId = await _submitReportAndOpenEmailDraft();
                if (!dialogContext.mounted) {
                  return;
                }
                setDialogState(() {
                  isSubmittingReport = false;
                  reportSubmitFailed = false;
                  reportSubmitStatus = strings.reportPrepared(
                    inboxEmail: _incidentReportInboxEmail,
                    reportId: reportId,
                  );
                });
              } catch (e) {
                if (!dialogContext.mounted) {
                  return;
                }
                setDialogState(() {
                  isSubmittingReport = false;
                  reportSubmitFailed = true;
                  reportSubmitStatus = strings.reportSendFailed(e);
                });
              }
            }

            return PopScope(
              canPop: false,
              child: AlertDialog(
                title: Row(
                  children: [
                    Icon(severityIcon, color: severityColor),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(alert.title),
                    ),
                  ],
                ),
                content: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 460),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        '$severityLabel | ${alert.reasonTag}',
                        style: TextStyle(
                          color: severityColor,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                      const SizedBox(height: 6),
                      Text(
                        '${strings.sourceLabel}: ${alert.source} ($queueName)',
                        style: TextStyle(
                          color: Colors.grey.shade700,
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      Text(
                        '${strings.occurredLabel}: $occurredLabel',
                        style: TextStyle(
                          color: Colors.grey.shade700,
                          fontSize: 12,
                        ),
                      ),
                      if (hasPending) ...[
                        const SizedBox(height: 4),
                        Text(
                          strings.queuePending(pendingAfter),
                          style: TextStyle(
                            color: Colors.grey.shade700,
                            fontSize: 12,
                          ),
                        ),
                      ],
                      const SizedBox(height: 10),
                      SelectableText(alert.message),
                      if (reportSubmitStatus != null) ...[
                        const SizedBox(height: 10),
                        Text(
                          reportSubmitStatus!,
                          style: TextStyle(
                            color: reportSubmitFailed
                                ? Colors.red.shade700
                                : Colors.green.shade700,
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
                actions: [
                  TextButton.icon(
                    onPressed: _history.isEmpty || isSubmittingReport
                        ? null
                        : () => unawaited(handleSubmitReport()),
                    icon: isSubmittingReport
                        ? const SizedBox(
                            width: 16,
                            height: 16,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(Icons.send_outlined),
                    label: Text(
                      isSubmittingReport
                          ? strings.sendingLabel
                          : strings.sendReportLabel,
                    ),
                  ),
                  FilledButton(
                    onPressed: () => Navigator.of(dialogContext).pop(),
                    child: Text(
                      hasPending
                          ? strings.nextLabel(pendingAfter)
                          : strings.closeLabel,
                    ),
                  ),
                ],
              ),
            );
          },
        );
      },
    );
  }

  static IconData _resolveSeverityIcon(OperatorIncidentSeverity severity) {
    switch (severity) {
      case OperatorIncidentSeverity.error:
        return Icons.error_outline;
      case OperatorIncidentSeverity.warning:
        return Icons.warning_amber_rounded;
      case OperatorIncidentSeverity.info:
        return Icons.info_outline;
    }
  }

  static Color _resolveSeverityColor(OperatorIncidentSeverity severity) {
    switch (severity) {
      case OperatorIncidentSeverity.error:
        return Colors.red.shade700;
      case OperatorIncidentSeverity.warning:
        return Colors.orange.shade800;
      case OperatorIncidentSeverity.info:
        return Colors.blue.shade700;
    }
  }
}

@immutable
class _OperatorIncidentStrings {
  final String noIncidentAlertsCaptured;
  final String incidentsLabel;
  final String noneLabel;
  final String mailTitle;
  final String eventSequenceLabel;
  final String sourceLabel;
  final String occurredLabel;
  final String sendingLabel;
  final String sendReportLabel;
  final String closeLabel;
  final String truncatedSuffix;
  final String _queuePendingTemplate;
  final String _nextTemplate;
  final String _cannotOpenEmailClientPrefix;
  final String _reportPreparedPrefix;
  final String _reportSendFailedPrefix;

  const _OperatorIncidentStrings({
    required this.noIncidentAlertsCaptured,
    required this.incidentsLabel,
    required this.noneLabel,
    required this.mailTitle,
    required this.eventSequenceLabel,
    required this.sourceLabel,
    required this.occurredLabel,
    required this.sendingLabel,
    required this.sendReportLabel,
    required this.closeLabel,
    required this.truncatedSuffix,
    required String queuePendingTemplate,
    required String nextTemplate,
    required String cannotOpenEmailClientPrefix,
    required String reportPreparedPrefix,
    required String reportSendFailedPrefix,
  })  : _queuePendingTemplate = queuePendingTemplate,
        _nextTemplate = nextTemplate,
        _cannotOpenEmailClientPrefix = cannotOpenEmailClientPrefix,
        _reportPreparedPrefix = reportPreparedPrefix,
        _reportSendFailedPrefix = reportSendFailedPrefix;

  String queuePending(int pendingAfter) =>
      _queuePendingTemplate.replaceFirst('{count}', pendingAfter.toString());

  String nextLabel(int pendingAfter) =>
      _nextTemplate.replaceFirst('{count}', pendingAfter.toString());

  String cannotOpenEmailClient({
    required String inboxEmail,
    required String reportId,
  }) {
    return '$_cannotOpenEmailClientPrefix$inboxEmail, reportId=$reportId.';
  }

  String reportPrepared({
    required String inboxEmail,
    required String reportId,
  }) {
    return '$_reportPreparedPrefix$inboxEmail. Report ID: $reportId';
  }

  String reportSendFailed(Object error) {
    return '$_reportSendFailedPrefix$error';
  }
}

_OperatorIncidentStrings _stringsFor(TherapistUiLanguage language) {
  switch (language) {
    case TherapistUiLanguage.polish:
      return const _OperatorIncidentStrings(
        noIncidentAlertsCaptured: 'Brak zapisanych alertow incydentow.',
        incidentsLabel: 'Incydenty',
        noneLabel: 'brak',
        mailTitle: 'Raport incydentow Theraply Mobile',
        eventSequenceLabel: 'Sekwencja zdarzen:',
        sourceLabel: 'Zrodlo',
        occurredLabel: 'Wystapilo',
        sendingLabel: 'Wysylanie...',
        sendReportLabel: 'Wyslij raport',
        closeLabel: 'Zamknij',
        truncatedSuffix:
            '\n\n[obcieto]\nPelny raport jest zapisany pod reportId w Firestore.',
        queuePendingTemplate: 'Kolejka: jeszcze {count} incydent(ow) oczekuje.',
        nextTemplate: 'Dalej ({count})',
        cannotOpenEmailClientPrefix:
            'Nie mozna otworzyc klienta email. Wyslij recznie na ',
        reportPreparedPrefix: 'Raport gotowy. Otworzono szkic email do ',
        reportSendFailedPrefix: 'Wysylka raportu nieudana: ',
      );
    case TherapistUiLanguage.english:
      return const _OperatorIncidentStrings(
        noIncidentAlertsCaptured: 'No incident alerts captured.',
        incidentsLabel: 'Incidents',
        noneLabel: 'none',
        mailTitle: 'Theraply Mobile Incident Report',
        eventSequenceLabel: 'Event sequence:',
        sourceLabel: 'Source',
        occurredLabel: 'Occurred',
        sendingLabel: 'Sending...',
        sendReportLabel: 'Send report',
        closeLabel: 'Close',
        truncatedSuffix:
            '\n\n[truncated]\nFull report is stored in Firestore under reportId.',
        queuePendingTemplate: 'Queue: {count} more incident(s) waiting.',
        nextTemplate: 'Next ({count})',
        cannotOpenEmailClientPrefix:
            'Cannot open email client. Send manually to ',
        reportPreparedPrefix: 'Report prepared. Email draft opened for ',
        reportSendFailedPrefix: 'Report send failed: ',
      );
  }
}
