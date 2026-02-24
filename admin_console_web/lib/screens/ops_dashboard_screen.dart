import 'dart:math';

import 'package:flutter/material.dart';
import 'package:admin_console_web/models/admin_directory_models.dart';
import 'package:admin_console_web/models/entitlement_access.dart';
import 'package:admin_console_web/models/entitlement_grant_contract.dart';
import 'package:admin_console_web/services/entitlement_admin_service.dart';
import 'package:admin_console_web/services/firebase_service.dart';

class OpsDashboardScreen extends StatefulWidget {
  const OpsDashboardScreen({super.key});

  @override
  State<OpsDashboardScreen> createState() => _OpsDashboardScreenState();
}

class _OpsDashboardScreenState extends State<OpsDashboardScreen> {
  static const List<_KnownGameEntry> _knownGames = <_KnownGameEntry>[
    _KnownGameEntry(
      gameId: 'smoke_test_game',
      title: 'Smoke Test Game',
      targetContentVersion: '1.0.0',
      description: 'Minimal connectivity and command smoke game.',
      packageUri: '',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 5,
      active: true,
      previewLines: <String>['Smoke validation and connectivity checks.'],
    ),
    _KnownGameEntry(
      gameId: 'demo_cube_clicker',
      title: 'Demo Cube Clicker',
      targetContentVersion: '1.2.0',
      description: 'Primary demo interaction game for resilience checks.',
      packageUri: '',
      thumbnailUrl: '',
      supportsSaveResume: true,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 10,
      active: true,
      previewLines: <String>[
        'Basic / alternation / random target color modes.'
      ],
    ),
    _KnownGameEntry(
      gameId: 'pulse_target_tap',
      title: 'Pulse Target Tap',
      targetContentVersion: '1.0.0',
      description: 'Second sample game with timed target taps.',
      packageUri: '',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: false,
      requiresExplicitLicense: false,
      runtimeLaunchEnabled: true,
      sortOrder: 20,
      active: true,
      previewLines: <String>['Adaptive difficulty and label pipeline.'],
    ),
    _KnownGameEntry(
      gameId: 'puzzle_paths',
      title: 'Puzzle Paths',
      targetContentVersion: '0.9.0',
      description: 'Store placeholder: puzzle session package.',
      packageUri: 'https://cdn.theraply.local/content/puzzle_paths_0_9_0',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: true,
      requiresExplicitLicense: true,
      runtimeLaunchEnabled: false,
      sortOrder: 30,
      active: true,
      previewLines: <String>['Catalog/store placeholder entry.'],
    ),
    _KnownGameEntry(
      gameId: 'memory_orchard',
      title: 'Memory Orchard',
      targetContentVersion: '0.9.0',
      description: 'Store placeholder: memory session package.',
      packageUri: 'https://cdn.theraply.local/content/memory_orchard_0_9_0',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: true,
      requiresExplicitLicense: true,
      runtimeLaunchEnabled: false,
      sortOrder: 40,
      active: true,
      previewLines: <String>['Catalog/store placeholder entry.'],
    ),
    _KnownGameEntry(
      gameId: 'sunflower_defense',
      title: 'Sunflower Defense',
      targetContentVersion: '0.9.0',
      description: 'Store placeholder: sunflower defense package.',
      packageUri: 'https://cdn.theraply.local/content/sunflower_defense_0_9_0',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: true,
      requiresExplicitLicense: true,
      runtimeLaunchEnabled: false,
      sortOrder: 50,
      active: true,
      previewLines: <String>['Catalog/store placeholder entry.'],
    ),
    _KnownGameEntry(
      gameId: 'coding_master',
      title: 'Coding Master',
      targetContentVersion: '0.9.0',
      description: 'Store placeholder: coding master package.',
      packageUri: 'https://cdn.theraply.local/content/coding_master_0_9_0',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: true,
      requiresExplicitLicense: true,
      runtimeLaunchEnabled: false,
      sortOrder: 60,
      active: true,
      previewLines: <String>['Catalog/store placeholder entry.'],
    ),
    _KnownGameEntry(
      gameId: 'bilateral_markers',
      title: 'Bilateral Markers',
      targetContentVersion: '0.9.0',
      description: 'Store placeholder: bilateral markers package.',
      packageUri: 'https://cdn.theraply.local/content/bilateral_markers_0_9_0',
      thumbnailUrl: '',
      supportsSaveResume: false,
      availableForPurchase: true,
      requiresExplicitLicense: true,
      runtimeLaunchEnabled: false,
      sortOrder: 70,
      active: true,
      previewLines: <String>['Catalog/store placeholder entry.'],
    ),
  ];

  final TextEditingController _targetUserIdController = TextEditingController();
  final TextEditingController _operationReasonController =
      TextEditingController();
  final TextEditingController _correlationIdController = TextEditingController(
    text: EntitlementAdminService.newCorrelationId(),
  );
  final TextEditingController _entitlementExpiresDaysController =
      TextEditingController(text: '365');
  final TextEditingController _planAllowedGameIdsController =
      TextEditingController();
  final TextEditingController _planDemoSessionLimitController =
      TextEditingController(text: '0');
  final TextEditingController _planDemoSessionsUsedController =
      TextEditingController(text: '0');
  final TextEditingController _grantGameIdController = TextEditingController();
  final TextEditingController _grantExpiresDaysController =
      TextEditingController(text: '30');
  final TextEditingController _grantNoteController = TextEditingController();

  EntitlementRole _selectedRole = EntitlementRole.therapist;
  SubscriptionPlanTier _selectedPlanTier = SubscriptionPlanTier.basic;
  LicenseStatus _selectedAppLicenseStatus = LicenseStatus.active;
  bool _entitlementPerpetual = true;

  EntitlementGrantScope _selectedGrantScope = EntitlementGrantScope.app;
  LicenseStatus _selectedGrantStatus = LicenseStatus.active;
  bool _grantPerpetual = true;
  bool _grantRevoked = false;
  EntitlementRole? _grantRoleOverride;

  bool _savingEntitlement = false;
  bool _savingGrant = false;
  bool _seedingGameCatalog = false;

  String get _targetUserId => _targetUserIdController.text.trim();

  @override
  void initState() {
    super.initState();
    _targetUserIdController.addListener(_refresh);
  }

  @override
  void dispose() {
    _targetUserIdController.removeListener(_refresh);
    _targetUserIdController.dispose();
    _operationReasonController.dispose();
    _correlationIdController.dispose();
    _entitlementExpiresDaysController.dispose();
    _planAllowedGameIdsController.dispose();
    _planDemoSessionLimitController.dispose();
    _planDemoSessionsUsedController.dispose();
    _grantGameIdController.dispose();
    _grantExpiresDaysController.dispose();
    _grantNoteController.dispose();
    super.dispose();
  }

  void _refresh() {
    setState(() {});
  }

  int _readDays(TextEditingController controller, int fallback) {
    final parsed = int.tryParse(controller.text.trim());
    if (parsed == null) {
      return fallback;
    }
    return max(1, parsed);
  }

  int _readNonNegativeInt(TextEditingController controller, int fallback) {
    final parsed = int.tryParse(controller.text.trim());
    if (parsed == null) {
      return fallback;
    }
    return max(0, parsed);
  }

  LicenseGrant _buildGrant({
    required LicenseStatus status,
    required bool perpetual,
    required int days,
  }) {
    final nowUtc = DateTime.now().toUtc();
    return LicenseGrant(
      status: status,
      fromUtc: nowUtc,
      toUtc: perpetual ? null : nowUtc.add(Duration(days: days)),
      perpetual: perpetual,
    );
  }

  EntitlementPlanProfile _buildPlanProfile({
    required SubscriptionPlanTier tier,
  }) {
    final allowedGameIds = _planAllowedGameIdsController.text
        .split(',')
        .map((value) => value.trim())
        .where((value) => value.isNotEmpty)
        .toSet();
    final demoLimit = _readNonNegativeInt(_planDemoSessionLimitController, 0);
    final demoUsed = _readNonNegativeInt(_planDemoSessionsUsedController, 0);

    final profile = EntitlementPlanProfile.fromMap(
      <String, dynamic>{
        'tier': tier.wireValue,
        'allowedGameIds': allowedGameIds.toList(),
        'demoSessionLimit': demoLimit,
        'demoSessionsUsed': demoUsed,
      },
    );
    return profile;
  }

  String _resolveCorrelationId() {
    final existing = _correlationIdController.text.trim();
    if (existing.isNotEmpty) {
      return existing;
    }

    final generated = EntitlementAdminService.newCorrelationId();
    _correlationIdController.text = generated;
    return generated;
  }

  void _rotateCorrelationId() {
    _correlationIdController.text = EntitlementAdminService.newCorrelationId();
  }

  String _resolveReason(String fallbackReason) {
    final existing = _operationReasonController.text.trim();
    if (existing.isNotEmpty) {
      return existing;
    }

    _operationReasonController.text = fallbackReason;
    return fallbackReason;
  }

  Future<void> _saveEntitlement() async {
    await _saveEntitlementInternal(
      role: _selectedRole,
      licenseStatus: _selectedAppLicenseStatus,
      perpetual: _entitlementPerpetual,
      expiresInDays: _readDays(_entitlementExpiresDaysController, 365),
      reasonFallback: 'manual-entitlement-update',
    );
  }

  Future<void> _saveEntitlementInternal({
    required EntitlementRole role,
    required LicenseStatus licenseStatus,
    required bool perpetual,
    required int expiresInDays,
    required String reasonFallback,
  }) async {
    if (_targetUserId.isEmpty) {
      _snack('Podaj UID uzytkownika', error: true);
      return;
    }

    final reason = _resolveReason(reasonFallback);

    final correlationId = _resolveCorrelationId();

    setState(() {
      _savingEntitlement = true;
    });

    try {
      await EntitlementAdminService.upsertUserEntitlement(
        userId: _targetUserId,
        role: role,
        appLicense: _buildGrant(
          status: licenseStatus,
          perpetual: perpetual,
          days: expiresInDays,
        ),
        planProfile: _buildPlanProfile(tier: _selectedPlanTier),
        reason: reason,
        correlationId: correlationId,
      );
      _snack('Zapisano user_entitlements/$_targetUserId ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Blad zapisu entitlement: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _savingEntitlement = false;
        });
      }
    }
  }

  Future<void> _useCurrentUserUidAsTarget() async {
    final currentUid = FirebaseService.currentUser?.uid;
    if (currentUid == null || currentUid.trim().isEmpty) {
      _snack('Brak zalogowanego UID', error: true);
      return;
    }

    _targetUserIdController.text = currentUid;
    _snack('Ustawiono target UID na aktualnego operatora');
  }

  Future<void> _quickGrantAppAccess() async {
    setState(() {
      _selectedRole = EntitlementRole.therapist;
      _selectedPlanTier = SubscriptionPlanTier.basic;
      _selectedAppLicenseStatus = LicenseStatus.active;
      _entitlementPerpetual = true;
    });

    await _saveEntitlementInternal(
      role: EntitlementRole.therapist,
      licenseStatus: LicenseStatus.active,
      perpetual: true,
      expiresInDays: 365,
      reasonFallback: 'quick-grant-app-access',
    );
  }

  Future<void> _quickRevokeAppAccess() async {
    setState(() {
      _selectedRole = EntitlementRole.therapist;
      _selectedPlanTier = SubscriptionPlanTier.basic;
      _selectedAppLicenseStatus = LicenseStatus.revoked;
      _entitlementPerpetual = true;
    });

    await _saveEntitlementInternal(
      role: EntitlementRole.therapist,
      licenseStatus: LicenseStatus.revoked,
      perpetual: true,
      expiresInDays: 365,
      reasonFallback: 'quick-revoke-app-access',
    );
  }

  Future<void> _createGrant() async {
    if (_targetUserId.isEmpty) {
      _snack('Podaj UID uzytkownika', error: true);
      return;
    }

    final gameId = _grantGameIdController.text.trim();
    if (_selectedGrantScope == EntitlementGrantScope.game && gameId.isEmpty) {
      _snack('Dla scope=GAME wymagany jest gameId', error: true);
      return;
    }

    final reason = _resolveReason('manual-grant-update');

    final correlationId = _resolveCorrelationId();

    setState(() {
      _savingGrant = true;
    });

    final nowUtc = DateTime.now().toUtc();
    try {
      final assignment = EntitlementGrantAssignment(
        grantId: '',
        granteeUserId: _targetUserId,
        scope: _selectedGrantScope,
        gameId:
            _selectedGrantScope == EntitlementGrantScope.game ? gameId : null,
        licenseGrant: _buildGrant(
          status: _selectedGrantStatus,
          perpetual: _grantPerpetual,
          days: _readDays(_grantExpiresDaysController, 30),
        ),
        source: EntitlementGrantSource.admin,
        assignedBy: FirebaseService.currentUser?.uid ?? 'admin-console-web',
        assignedAtUtc: nowUtc,
        revoked: _grantRevoked,
        revokedAtUtc: _grantRevoked ? nowUtc : null,
        roleOverride: _grantRoleOverride,
        note: _grantNoteController.text.trim().isEmpty
            ? null
            : _grantNoteController.text.trim(),
        reason: reason,
        correlationId: correlationId,
      );

      final grantId = await EntitlementAdminService.upsertGrantAssignment(
        assignment: assignment,
        reason: reason,
        correlationId: correlationId,
      );
      _snack('Utworzono grant $grantId ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Blad zapisu grantu: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _savingGrant = false;
        });
      }
    }
  }

  Future<void> _revokeGrant(EntitlementGrantAssignment grant) async {
    final reason = _resolveReason('manual-grant-revoke');

    final correlationId = _resolveCorrelationId();
    final nowUtc = DateTime.now().toUtc();
    try {
      await EntitlementAdminService.upsertGrantAssignment(
        assignment: EntitlementGrantAssignment(
          grantId: grant.grantId,
          granteeUserId: grant.granteeUserId,
          scope: grant.scope,
          gameId: grant.gameId,
          licenseGrant: grant.licenseGrant,
          source: grant.source,
          assignedBy: grant.assignedBy,
          assignedAtUtc: grant.assignedAtUtc,
          revoked: true,
          revokedAtUtc: nowUtc,
          roleOverride: grant.roleOverride,
          note: grant.note,
          reason: reason,
          correlationId: correlationId,
        ),
        reason: reason,
        correlationId: correlationId,
        action: 'REVOKE_GRANT_ASSIGNMENT',
      );
      _snack('Grant ${grant.grantId} revoked ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Blad revoke: $e', error: true);
    }
  }

  Future<void> _seedGameCatalog() async {
    final reason = _resolveReason('seed-game-catalog');
    final correlationId = _resolveCorrelationId();

    setState(() {
      _seedingGameCatalog = true;
    });

    try {
      final payload = _knownGames
          .map(
            (entry) => AdminGameCatalogSeedEntry(
              gameId: entry.gameId,
              title: entry.title,
              description: entry.description,
              targetContentVersion: entry.targetContentVersion,
              packageUri: entry.packageUri,
              thumbnailUrl: entry.thumbnailUrl,
              supportsSaveResume: entry.supportsSaveResume,
              availableForPurchase: entry.availableForPurchase,
              requiresExplicitLicense: entry.requiresExplicitLicense,
              runtimeLaunchEnabled: entry.runtimeLaunchEnabled,
              sortOrder: entry.sortOrder,
              active: entry.active,
              previewLines: entry.previewLines,
            ),
          )
          .toList(growable: false);

      await EntitlementAdminService.seedGameCatalog(
        entries: payload,
        reason: reason,
        correlationId: correlationId,
      );

      _snack(
          'Seeded game_catalog (${payload.length} entries) [$correlationId]');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Seeding game_catalog failed: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _seedingGameCatalog = false;
        });
      }
    }
  }

  void _snack(String message, {bool error = false}) {
    if (!mounted) {
      return;
    }
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
        backgroundColor: error ? Colors.red.shade700 : null,
      ),
    );
  }

  String _formatUtc(DateTime? value) {
    if (value == null) {
      return '-';
    }

    final datePart = value.toIso8601String().replaceFirst('T', ' ');
    return datePart.endsWith('Z') ? datePart : '${datePart}Z';
  }

  void _useTargetUid(String uid) {
    final normalizedUid = uid.trim();
    if (normalizedUid.isEmpty) {
      return;
    }

    _targetUserIdController.text = normalizedUid;
    _snack('Ustawiono Target UID: $normalizedUid');
  }

  void _useGameIdForGrant(String gameId) {
    final normalizedGameId = gameId.trim();
    if (normalizedGameId.isEmpty) {
      return;
    }

    setState(() {
      _selectedGrantScope = EntitlementGrantScope.game;
      _grantGameIdController.text = normalizedGameId;
    });

    final tabController = DefaultTabController.of(context);
    tabController.animateTo(0);
    _snack('Ustawiono grant GAME dla gameId=$normalizedGameId');
  }

  Widget _buildOperationsTab() {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        TextField(
          controller: _targetUserIdController,
          decoration: const InputDecoration(
            labelText: 'Target User UID',
            border: OutlineInputBorder(),
            helperText:
                'Firebase -> Authentication -> Users -> skopiuj User UID',
          ),
        ),
        const SizedBox(height: 12),
        _buildQuickActionsCard(),
        const SizedBox(height: 12),
        _buildAuditCard(),
        const SizedBox(height: 12),
        _buildEntitlementCard(),
        const SizedBox(height: 12),
        _buildGrantCard(),
        const SizedBox(height: 12),
        _buildLiveCard(),
      ],
    );
  }

  Widget _buildAccountsTab() {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        _buildRoleDirectoryCard(
          title: 'Therapists',
          role: EntitlementRole.therapist,
          emptyLabel: 'Brak terapeutow w user_entitlements.',
        ),
        const SizedBox(height: 12),
        _buildRoleDirectoryCard(
          title: 'Parents',
          role: EntitlementRole.parent,
          emptyLabel: 'Brak rodzicow w user_entitlements.',
        ),
      ],
    );
  }

  Widget _buildRoleDirectoryCard({
    required String title,
    required EntitlementRole role,
    required String emptyLabel,
  }) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              title,
              style: const TextStyle(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            StreamBuilder<List<AdminDirectoryUserRow>>(
              stream: EntitlementAdminService.watchUsersByRole(role),
              builder: (context, snapshot) {
                if (snapshot.hasError) {
                  return Text(
                    'Blad odczytu: ${snapshot.error}',
                    style: TextStyle(color: Colors.red.shade700),
                  );
                }

                if (!snapshot.hasData) {
                  return const Center(
                    child: Padding(
                      padding: EdgeInsets.all(12),
                      child: CircularProgressIndicator(),
                    ),
                  );
                }

                final users = snapshot.data!;
                if (users.isEmpty) {
                  return Text(emptyLabel);
                }

                return Column(
                  children: [
                    Align(
                      alignment: Alignment.centerLeft,
                      child: Text('Count: ${users.length}'),
                    ),
                    const SizedBox(height: 8),
                    for (final user in users)
                      Container(
                        margin: const EdgeInsets.only(bottom: 8),
                        padding: const EdgeInsets.all(8),
                        decoration: BoxDecoration(
                          color: Colors.grey.shade100,
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    user.userId,
                                    style: const TextStyle(
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                  const SizedBox(height: 4),
                                  Text(
                                    'Role=${user.role.wireValue}, '
                                    'Plan=${user.planTier.wireValue}, '
                                    'App=${user.appLicenseStatus.wireValue}, '
                                    'Updated=${_formatUtc(user.updatedAtUtc)}',
                                    style: const TextStyle(fontSize: 12),
                                  ),
                                  if (user.updatedBy != null &&
                                      user.updatedBy!.trim().isNotEmpty)
                                    Text(
                                      'UpdatedBy=${user.updatedBy}',
                                      style: const TextStyle(fontSize: 12),
                                    ),
                                ],
                              ),
                            ),
                            TextButton(
                              onPressed: () => _useTargetUid(user.userId),
                              child: const Text('Use UID'),
                            ),
                          ],
                        ),
                      ),
                  ],
                );
              },
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildChildrenTab() {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'Children / Patients',
                  style: TextStyle(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 8),
                StreamBuilder<List<AdminStudentDirectoryRow>>(
                  stream: EntitlementAdminService.watchStudents(),
                  builder: (context, snapshot) {
                    if (snapshot.hasError) {
                      return Text(
                        'Blad odczytu: ${snapshot.error}',
                        style: TextStyle(color: Colors.red.shade700),
                      );
                    }

                    if (!snapshot.hasData) {
                      return const Center(
                        child: Padding(
                          padding: EdgeInsets.all(12),
                          child: CircularProgressIndicator(),
                        ),
                      );
                    }

                    final students = snapshot.data!;
                    if (students.isEmpty) {
                      return const Text('Brak rekordow students.');
                    }

                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Count: ${students.length}'),
                        const SizedBox(height: 8),
                        for (final student in students)
                          Container(
                            margin: const EdgeInsets.only(bottom: 8),
                            padding: const EdgeInsets.all(8),
                            decoration: BoxDecoration(
                              color: Colors.grey.shade100,
                              borderRadius: BorderRadius.circular(8),
                            ),
                            child: Row(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Expanded(
                                  child: Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      Text(
                                        student.fullName,
                                        style: const TextStyle(
                                          fontWeight: FontWeight.w600,
                                        ),
                                      ),
                                      const SizedBox(height: 4),
                                      Text(
                                        'StudentId=${student.studentId}',
                                        style: const TextStyle(fontSize: 12),
                                      ),
                                      Text(
                                        'Owner therapist UID=${student.therapistId}',
                                        style: const TextStyle(fontSize: 12),
                                      ),
                                      Text(
                                        'Updated=${_formatUtc(student.updatedAtUtc)}',
                                        style: const TextStyle(fontSize: 12),
                                      ),
                                    ],
                                  ),
                                ),
                                TextButton(
                                  onPressed: () =>
                                      _useTargetUid(student.therapistId),
                                  child: const Text('Use owner UID'),
                                ),
                              ],
                            ),
                          ),
                      ],
                    );
                  },
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildGamesTab() {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        Card(
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text(
                  'Known Games',
                  style: TextStyle(fontWeight: FontWeight.w700),
                ),
                const SizedBox(height: 8),
                const Text(
                  'Known catalog used by mobile store/installed tabs. You can seed game_catalog with one click.',
                ),
                const SizedBox(height: 10),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    ElevatedButton.icon(
                      onPressed: _seedingGameCatalog ? null : _seedGameCatalog,
                      icon: _seedingGameCatalog
                          ? const SizedBox(
                              width: 14,
                              height: 14,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.cloud_upload_outlined),
                      label: Text(
                        _seedingGameCatalog
                            ? 'Seeding...'
                            : 'Seed game_catalog',
                      ),
                    ),
                    OutlinedButton.icon(
                      onPressed: _useCurrentUserUidAsTarget,
                      icon: const Icon(Icons.person_pin),
                      label: const Text('Use my UID'),
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                StreamBuilder<Map<String, AdminGameGrantStats>>(
                  stream: EntitlementAdminService.watchGameGrantStats(),
                  builder: (context, snapshot) {
                    final statsByGameId =
                        snapshot.data ?? const <String, AdminGameGrantStats>{};
                    final knownIds =
                        _knownGames.map((entry) => entry.gameId).toSet();
                    final unknownGrantGames = statsByGameId.keys
                        .where((gameId) => !knownIds.contains(gameId))
                        .toList()
                      ..sort();

                    return Column(
                      children: [
                        for (final game in _knownGames)
                          _buildGameCard(
                            title: game.title,
                            gameId: game.gameId,
                            description: game.description,
                            targetVersion: game.targetContentVersion,
                            packageUri: game.packageUri,
                            availableForPurchase: game.availableForPurchase,
                            runtimeLaunchEnabled: game.runtimeLaunchEnabled,
                            sortOrder: game.sortOrder,
                            stats: statsByGameId[game.gameId],
                          ),
                        if (unknownGrantGames.isNotEmpty) ...[
                          const SizedBox(height: 8),
                          const Align(
                            alignment: Alignment.centerLeft,
                            child: Text(
                              'Granty dla innych gameId:',
                              style: TextStyle(fontWeight: FontWeight.w600),
                            ),
                          ),
                          const SizedBox(height: 8),
                          for (final gameId in unknownGrantGames)
                            _buildGameCard(
                              title: gameId,
                              gameId: gameId,
                              description: 'GAME scope detected in grants.',
                              targetVersion: '-',
                              packageUri: '',
                              availableForPurchase: false,
                              runtimeLaunchEnabled: false,
                              sortOrder: 0,
                              stats: statsByGameId[gameId],
                            ),
                        ],
                      ],
                    );
                  },
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Widget _buildGameCard({
    required String title,
    required String gameId,
    required String description,
    required String targetVersion,
    required String packageUri,
    required bool availableForPurchase,
    required bool runtimeLaunchEnabled,
    required int sortOrder,
    required AdminGameGrantStats? stats,
  }) {
    final activeCount = stats?.activeAssignments ?? 0;
    final revokedCount = stats?.revokedAssignments ?? 0;
    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(8),
      decoration: BoxDecoration(
        color: Colors.grey.shade100,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: 4),
                Text('gameId=$gameId', style: const TextStyle(fontSize: 12)),
                Text(
                  'targetVersion=$targetVersion',
                  style: const TextStyle(fontSize: 12),
                ),
                Text(
                  'sortOrder=$sortOrder | purchasable=$availableForPurchase | launchEnabled=$runtimeLaunchEnabled',
                  style: const TextStyle(fontSize: 12),
                ),
                if (packageUri.trim().isNotEmpty)
                  Text(
                    'packageUri=$packageUri',
                    style: const TextStyle(fontSize: 12),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                  ),
                Text(description, style: const TextStyle(fontSize: 12)),
                Text(
                  'Grant stats: active=$activeCount, revoked=$revokedCount',
                  style: const TextStyle(fontSize: 12),
                ),
              ],
            ),
          ),
          TextButton(
            onPressed: () => _useGameIdForGrant(gameId),
            child: const Text('Use gameId'),
          ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 4,
      child: Scaffold(
        appBar: AppBar(
          title: const Text('Theraply Entitlement Admin'),
          actions: [
            if (FirebaseService.currentUser != null)
              Padding(
                padding: const EdgeInsets.only(right: 12),
                child: Center(
                  child: Text(
                    FirebaseService.currentUser!.email ??
                        FirebaseService.currentUser!.uid,
                    style: const TextStyle(fontSize: 12),
                  ),
                ),
              ),
            IconButton(
              onPressed: FirebaseService.signOut,
              icon: const Icon(Icons.logout),
              tooltip: 'Logout',
            ),
          ],
          bottom: const TabBar(
            isScrollable: true,
            tabs: [
              Tab(text: 'Operations'),
              Tab(text: 'Therapists/Parents'),
              Tab(text: 'Children'),
              Tab(text: 'Games'),
            ],
          ),
        ),
        body: TabBarView(
          children: [
            _buildOperationsTab(),
            _buildAccountsTab(),
            _buildChildrenTab(),
            _buildGamesTab(),
          ],
        ),
      ),
    );
  }

  Widget _buildQuickActionsCard() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Quick Test Actions (CMS-lite)',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            const Text(
              'Najczestsze testy: ustaw target UID i kliknij szybka akcje.',
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                OutlinedButton.icon(
                  onPressed: _useCurrentUserUidAsTarget,
                  icon: const Icon(Icons.person),
                  label: const Text('Use my UID'),
                ),
                FilledButton.tonalIcon(
                  onPressed: _savingEntitlement ? null : _quickGrantAppAccess,
                  icon: const Icon(Icons.check_circle),
                  label: const Text('Quick: grant app access'),
                ),
                FilledButton.tonalIcon(
                  onPressed: _savingEntitlement ? null : _quickRevokeAppAccess,
                  icon: const Icon(Icons.block),
                  label: const Text('Quick: revoke app access'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildAuditCard() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Audit Context (required)',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _operationReasonController,
              decoration: const InputDecoration(
                labelText: 'Reason',
                hintText: 'e.g. therapist-license-renewal',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _correlationIdController,
              decoration: InputDecoration(
                labelText: 'Correlation ID',
                border: const OutlineInputBorder(),
                suffixIcon: IconButton(
                  onPressed: _rotateCorrelationId,
                  tooltip: 'Generate new',
                  icon: const Icon(Icons.refresh),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildEntitlementCard() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'user_entitlements',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            DropdownButtonFormField<EntitlementRole>(
              initialValue: _selectedRole,
              decoration: const InputDecoration(
                labelText: 'Role',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(
                  value: EntitlementRole.therapist,
                  child: Text('THERAPIST'),
                ),
                DropdownMenuItem(
                  value: EntitlementRole.parent,
                  child: Text('PARENT'),
                ),
              ],
              onChanged: (value) {
                if (value == null) return;
                setState(() {
                  _selectedRole = value;
                  if (value == EntitlementRole.parent &&
                      _selectedPlanTier == SubscriptionPlanTier.basic) {
                    _selectedPlanTier = SubscriptionPlanTier.free;
                  } else if (value == EntitlementRole.therapist &&
                      _selectedPlanTier == SubscriptionPlanTier.free) {
                    _selectedPlanTier = SubscriptionPlanTier.basic;
                  }
                });
              },
            ),
            const SizedBox(height: 8),
            DropdownButtonFormField<SubscriptionPlanTier>(
              initialValue: _selectedPlanTier,
              decoration: const InputDecoration(
                labelText: 'Subscription Plan',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(
                  value: SubscriptionPlanTier.free,
                  child: Text('FREE'),
                ),
                DropdownMenuItem(
                  value: SubscriptionPlanTier.basic,
                  child: Text('BASIC'),
                ),
                DropdownMenuItem(
                  value: SubscriptionPlanTier.premium,
                  child: Text('PREMIUM'),
                ),
                DropdownMenuItem(
                  value: SubscriptionPlanTier.live,
                  child: Text('LIVE'),
                ),
              ],
              onChanged: (value) {
                if (value == null) return;
                setState(() {
                  _selectedPlanTier = value;
                });
              },
            ),
            const SizedBox(height: 8),
            TextField(
              controller: _planAllowedGameIdsController,
              decoration: const InputDecoration(
                labelText: 'Allowed gameIds (CSV, optional)',
                hintText: 'demo_cube_clicker,pulse_target_tap',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 8),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _planDemoSessionLimitController,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'Demo session limit (0=unlimited)',
                      border: OutlineInputBorder(),
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: TextField(
                    controller: _planDemoSessionsUsedController,
                    keyboardType: TextInputType.number,
                    decoration: const InputDecoration(
                      labelText: 'Demo sessions used',
                      border: OutlineInputBorder(),
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            DropdownButtonFormField<LicenseStatus>(
              initialValue: _selectedAppLicenseStatus,
              decoration: const InputDecoration(
                labelText: 'App License Status',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(
                    value: LicenseStatus.active, child: Text('ACTIVE')),
                DropdownMenuItem(
                    value: LicenseStatus.expired, child: Text('EXPIRED')),
                DropdownMenuItem(
                    value: LicenseStatus.revoked, child: Text('REVOKED')),
                DropdownMenuItem(
                    value: LicenseStatus.none, child: Text('NONE')),
              ],
              onChanged: (value) {
                if (value == null) return;
                setState(() {
                  _selectedAppLicenseStatus = value;
                });
              },
            ),
            SwitchListTile(
              value: _entitlementPerpetual,
              onChanged: (value) {
                setState(() {
                  _entitlementPerpetual = value;
                });
              },
              contentPadding: EdgeInsets.zero,
              title: const Text('Perpetual'),
            ),
            if (!_entitlementPerpetual)
              TextField(
                controller: _entitlementExpiresDaysController,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Expires In Days',
                  border: OutlineInputBorder(),
                ),
              ),
            const SizedBox(height: 8),
            FilledButton.icon(
              onPressed: _savingEntitlement ? null : _saveEntitlement,
              icon: _savingEntitlement
                  ? const SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.save),
              label: const Text('Save Entitlement'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildGrantCard() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'entitlement_grants',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            DropdownButtonFormField<EntitlementGrantScope>(
              initialValue: _selectedGrantScope,
              decoration: const InputDecoration(
                labelText: 'Scope',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(
                    value: EntitlementGrantScope.app, child: Text('APP')),
                DropdownMenuItem(
                    value: EntitlementGrantScope.game, child: Text('GAME')),
              ],
              onChanged: (value) {
                if (value == null) return;
                setState(() {
                  _selectedGrantScope = value;
                });
              },
            ),
            const SizedBox(height: 8),
            if (_selectedGrantScope == EntitlementGrantScope.game)
              TextField(
                controller: _grantGameIdController,
                decoration: const InputDecoration(
                  labelText: 'gameId',
                  border: OutlineInputBorder(),
                ),
              ),
            if (_selectedGrantScope == EntitlementGrantScope.game)
              const SizedBox(height: 8),
            DropdownButtonFormField<LicenseStatus>(
              initialValue: _selectedGrantStatus,
              decoration: const InputDecoration(
                labelText: 'Grant Status',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(
                    value: LicenseStatus.active, child: Text('ACTIVE')),
                DropdownMenuItem(
                    value: LicenseStatus.expired, child: Text('EXPIRED')),
                DropdownMenuItem(
                    value: LicenseStatus.revoked, child: Text('REVOKED')),
                DropdownMenuItem(
                    value: LicenseStatus.none, child: Text('NONE')),
              ],
              onChanged: (value) {
                if (value == null) return;
                setState(() {
                  _selectedGrantStatus = value;
                });
              },
            ),
            const SizedBox(height: 8),
            DropdownButtonFormField<EntitlementRole?>(
              initialValue: _grantRoleOverride,
              decoration: const InputDecoration(
                labelText: 'Role Override (optional)',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem<EntitlementRole?>(
                  value: null,
                  child: Text('NONE'),
                ),
                DropdownMenuItem<EntitlementRole?>(
                  value: EntitlementRole.therapist,
                  child: Text('THERAPIST'),
                ),
                DropdownMenuItem<EntitlementRole?>(
                  value: EntitlementRole.parent,
                  child: Text('PARENT'),
                ),
              ],
              onChanged: (value) {
                setState(() {
                  _grantRoleOverride = value;
                });
              },
            ),
            SwitchListTile(
              value: _grantPerpetual,
              onChanged: (value) {
                setState(() {
                  _grantPerpetual = value;
                });
              },
              contentPadding: EdgeInsets.zero,
              title: const Text('Perpetual'),
            ),
            if (!_grantPerpetual)
              TextField(
                controller: _grantExpiresDaysController,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Grant Expires In Days',
                  border: OutlineInputBorder(),
                ),
              ),
            SwitchListTile(
              value: _grantRevoked,
              onChanged: (value) {
                setState(() {
                  _grantRevoked = value;
                });
              },
              contentPadding: EdgeInsets.zero,
              title: const Text('Create As Revoked'),
            ),
            TextField(
              controller: _grantNoteController,
              decoration: const InputDecoration(
                labelText: 'Note (optional)',
                border: OutlineInputBorder(),
              ),
              maxLines: 2,
            ),
            const SizedBox(height: 8),
            FilledButton.icon(
              onPressed: _savingGrant ? null : _createGrant,
              icon: _savingGrant
                  ? const SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.add),
              label: const Text('Create Grant'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildLiveCard() {
    if (_targetUserId.isEmpty) {
      return const Card(
        child: Padding(
          padding: EdgeInsets.all(12),
          child: Text('Wpisz UID, aby zobaczyc aktualny stan danych.'),
        ),
      );
    }

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Live Snapshot',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            StreamBuilder<EntitlementAccess?>(
              stream: EntitlementAdminService.watchEntitlementProfile(
                  _targetUserId),
              builder: (context, snapshot) {
                final profile = snapshot.data;
                if (profile == null) {
                  return const Text('Entitlement profile: brak');
                }

                return Text(
                  'Role=${profile.role.wireValue}, '
                  'App=${profile.appLicense.status.wireValue}, '
                  'Plan=${profile.planProfile.tier.wireValue}, '
                  'Demo=${profile.planProfile.demoSessionsUsed}/${profile.planProfile.demoSessionLimit}, '
                  'Policy=${profile.policyVersion ?? '-'}',
                );
              },
            ),
            const SizedBox(height: 10),
            StreamBuilder<List<EntitlementGrantAssignment>>(
              stream: EntitlementAdminService.watchGrantAssignmentsForUser(
                _targetUserId,
              ),
              builder: (context, snapshot) {
                final grants = snapshot.data ?? <EntitlementGrantAssignment>[];
                if (grants.isEmpty) {
                  return const Text('Granty: brak');
                }

                return Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Granty: ${grants.length}'),
                    const SizedBox(height: 6),
                    for (final grant in grants)
                      Container(
                        margin: const EdgeInsets.only(bottom: 6),
                        padding: const EdgeInsets.all(8),
                        decoration: BoxDecoration(
                          color: Colors.grey.shade100,
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Row(
                          children: [
                            Expanded(
                              child: Text(
                                '${grant.scope.wireValue} '
                                '${grant.gameId ?? '-'} | '
                                '${grant.licenseGrant.status.wireValue} | '
                                '${grant.revoked ? 'REVOKED' : 'ACTIVE'}',
                                style: const TextStyle(fontSize: 12),
                              ),
                            ),
                            if (!grant.revoked)
                              TextButton(
                                onPressed: () => _revokeGrant(grant),
                                child: const Text('Revoke'),
                              ),
                          ],
                        ),
                      ),
                  ],
                );
              },
            ),
          ],
        ),
      ),
    );
  }
}

class _KnownGameEntry {
  final String gameId;
  final String title;
  final String targetContentVersion;
  final String description;
  final String packageUri;
  final String thumbnailUrl;
  final bool supportsSaveResume;
  final bool availableForPurchase;
  final bool requiresExplicitLicense;
  final bool runtimeLaunchEnabled;
  final int sortOrder;
  final bool active;
  final List<String> previewLines;

  const _KnownGameEntry({
    required this.gameId,
    required this.title,
    required this.targetContentVersion,
    required this.description,
    required this.packageUri,
    required this.thumbnailUrl,
    required this.supportsSaveResume,
    required this.availableForPurchase,
    required this.requiresExplicitLicense,
    required this.runtimeLaunchEnabled,
    required this.sortOrder,
    required this.active,
    required this.previewLines,
  });
}
