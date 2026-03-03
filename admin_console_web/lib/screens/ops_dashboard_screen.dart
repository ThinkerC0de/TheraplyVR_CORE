import 'dart:math';

import 'package:flutter/material.dart';
import 'package:admin_console_web/models/admin_directory_models.dart';
import 'package:admin_console_web/models/entitlement_access.dart';
import 'package:admin_console_web/models/entitlement_grant_contract.dart';
import 'package:admin_console_web/services/entitlement_admin_service.dart';
import 'package:admin_console_web/services/firebase_service.dart';
import 'package:admin_console_web/services/game_catalog_seed_source.dart';

class OpsDashboardScreen extends StatefulWidget {
  const OpsDashboardScreen({super.key});

  @override
  State<OpsDashboardScreen> createState() => _OpsDashboardScreenState();
}

class _OpsDashboardScreenState extends State<OpsDashboardScreen> {
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
  final TextEditingController _sessionIdFilterController =
      TextEditingController();

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
  bool _savingCatalogEntry = false;
  bool _savingAccountDirectory = false;
  bool _savingStudentDirectory = false;
  bool _deletingSessions = false;
  bool _seedingGameCatalog = false;
  bool _loadingCatalogSeed = true;
  bool _loadingExportManifest = true;
  String _catalogSeedError = '';
  String _exportManifestError = '';
  List<AdminGameCatalogSeedEntry> _catalogSeedEntries =
      const <AdminGameCatalogSeedEntry>[];
  GameDefinitionExportManifest? _exportManifest;
  _GamesAuthoringFilter _gamesAuthoringFilter = _GamesAuthoringFilter.all;
  _GamesSortMode _gamesSortMode = _GamesSortMode.authoringSeverity;
  _SessionStateFilter _sessionStateFilter = _SessionStateFilter.all;
  bool _anonymizeSessionData = false;
  String _sessionTherapistFilter = '';
  String _sessionStudentFilter = '';
  String _selectedSessionDocumentId = '';
  final Set<String> _selectedSessionDocumentIds = <String>{};

  String get _targetUserId => _targetUserIdController.text.trim();

  @override
  void initState() {
    super.initState();
    _targetUserIdController.addListener(_refresh);
    _sessionIdFilterController.addListener(_refresh);
    _loadCatalogAuthoringAssets();
  }

  @override
  void dispose() {
    _targetUserIdController.removeListener(_refresh);
    _sessionIdFilterController.removeListener(_refresh);
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
    _sessionIdFilterController.dispose();
    super.dispose();
  }

  void _refresh() {
    setState(() {});
  }

  Future<void> _loadCatalogSeed() async {
    setState(() {
      _loadingCatalogSeed = true;
      _catalogSeedError = '';
    });

    try {
      final entries = await GameCatalogSeedSource.loadDefault();
      if (!mounted) {
        return;
      }

      setState(() {
        _catalogSeedEntries = entries;
      });
    } catch (error) {
      if (!mounted) {
        return;
      }

      setState(() {
        _catalogSeedEntries = const <AdminGameCatalogSeedEntry>[];
        _catalogSeedError = error.toString();
      });
    } finally {
      if (mounted) {
        setState(() {
          _loadingCatalogSeed = false;
        });
      }
    }
  }

  Future<void> _loadExportManifest() async {
    setState(() {
      _loadingExportManifest = true;
      _exportManifestError = '';
    });

    try {
      final manifest = await GameCatalogSeedSource.loadManifestDefault();
      if (!mounted) {
        return;
      }

      setState(() {
        _exportManifest = manifest;
      });
    } catch (error) {
      if (!mounted) {
        return;
      }

      setState(() {
        _exportManifest = null;
        _exportManifestError = error.toString();
      });
    } finally {
      if (mounted) {
        setState(() {
          _loadingExportManifest = false;
        });
      }
    }
  }

  Future<void> _loadCatalogAuthoringAssets() async {
    await Future.wait<void>(<Future<void>>[
      _loadCatalogSeed(),
      _loadExportManifest(),
    ]);
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
    if (_loadingCatalogSeed) {
      _snack('Catalog seed loading in progress.', error: true);
      return;
    }

    if (_catalogSeedEntries.isEmpty) {
      _snack(
        _catalogSeedError.isEmpty
            ? 'Catalog seed is empty. Check assets/contracts/game_catalog_seed.json.'
            : 'Catalog seed unavailable: $_catalogSeedError',
        error: true,
      );
      return;
    }

    final reason = _resolveReason('seed-game-catalog');
    final correlationId = _resolveCorrelationId();

    setState(() {
      _seedingGameCatalog = true;
    });

    try {
      final payload = List<AdminGameCatalogSeedEntry>.from(
        _catalogSeedEntries,
        growable: false,
      );

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

  Future<AdminGameCatalogSeedEntry?> _openGameCatalogEditorDialog({
    AdminGameCatalogSeedEntry? existing,
  }) async {
    final isCreate = existing == null;
    final gameIdController =
        TextEditingController(text: existing?.gameId ?? '');
    final titleController = TextEditingController(text: existing?.title ?? '');
    final descriptionController =
        TextEditingController(text: existing?.description ?? '');
    final targetVersionController =
        TextEditingController(text: existing?.targetContentVersion ?? '');
    final contentVersionController =
        TextEditingController(text: existing?.contentVersion ?? '');
    final sceneKeyController =
        TextEditingController(text: existing?.sceneKey ?? '');
    final entitlementKeyController =
        TextEditingController(text: existing?.entitlementKey ?? '');
    final packageUriController =
        TextEditingController(text: existing?.packageUri ?? '');
    final thumbnailUrlController =
        TextEditingController(text: existing?.thumbnailUrl ?? '');
    final sortOrderController = TextEditingController(
      text: (existing?.sortOrder ?? 0).toString(),
    );
    final previewLinesController = TextEditingController(
      text: (existing?.previewLines ?? const <String>[]).join('\n'),
    );

    var active = existing?.active ?? true;
    var runtimeLaunchEnabled = existing?.runtimeLaunchEnabled ?? true;
    var availableForPurchase = existing?.availableForPurchase ?? false;
    var requiresExplicitLicense = existing?.requiresExplicitLicense ?? false;
    var supportsSaveResume = existing?.supportsSaveResume ?? false;
    var deliveryMode =
        existing?.deliveryMode ?? AdminGameCatalogSeedEntry.deliveryModeBundled;
    var validationError = '';
    var showAdvanced = !isCreate;

    final result = await showDialog<AdminGameCatalogSeedEntry>(
      context: context,
      builder: (dialogContext) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            return AlertDialog(
              title: Text(
                isCreate
                    ? 'Create game_catalog entry'
                    : 'Edit game_catalog entry',
              ),
              content: SizedBox(
                width: 620,
                child: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      TextField(
                        controller: gameIdController,
                        enabled: isCreate,
                        decoration: const InputDecoration(
                          labelText: 'gameId',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      TextField(
                        controller: titleController,
                        decoration: const InputDecoration(
                          labelText: 'title',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      TextField(
                        controller: descriptionController,
                        maxLines: 2,
                        decoration: const InputDecoration(
                          labelText: 'description',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      TextField(
                        controller: targetVersionController,
                        decoration: const InputDecoration(
                          labelText: 'targetContentVersion',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      TextField(
                        controller: packageUriController,
                        decoration: const InputDecoration(
                          labelText: 'packageUri',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      TextField(
                        controller: sortOrderController,
                        keyboardType: TextInputType.number,
                        decoration: const InputDecoration(
                          labelText: 'sortOrder',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      DropdownButtonFormField<String>(
                        initialValue: deliveryMode,
                        decoration: const InputDecoration(
                          labelText: 'deliveryMode',
                          border: OutlineInputBorder(),
                        ),
                        items: const [
                          DropdownMenuItem(
                            value:
                                AdminGameCatalogSeedEntry.deliveryModeBundled,
                            child: Text('bundled'),
                          ),
                          DropdownMenuItem(
                            value:
                                AdminGameCatalogSeedEntry.deliveryModeOnDemand,
                            child: Text('on_demand'),
                          ),
                        ],
                        onChanged: (value) {
                          if (value == null) {
                            return;
                          }
                          setDialogState(() {
                            deliveryMode = value;
                          });
                        },
                      ),
                      const SizedBox(height: 8),
                      SwitchListTile(
                        value: active,
                        onChanged: (value) {
                          setDialogState(() {
                            active = value;
                          });
                        },
                        contentPadding: EdgeInsets.zero,
                        title: const Text('active'),
                      ),
                      SwitchListTile(
                        value: runtimeLaunchEnabled,
                        onChanged: (value) {
                          setDialogState(() {
                            runtimeLaunchEnabled = value;
                          });
                        },
                        contentPadding: EdgeInsets.zero,
                        title: const Text('runtimeLaunchEnabled'),
                      ),
                      SwitchListTile(
                        value: availableForPurchase,
                        onChanged: (value) {
                          setDialogState(() {
                            availableForPurchase = value;
                          });
                        },
                        contentPadding: EdgeInsets.zero,
                        title: const Text('availableForPurchase'),
                      ),
                      Align(
                        alignment: Alignment.centerLeft,
                        child: OutlinedButton.icon(
                          onPressed: () {
                            setDialogState(() {
                              showAdvanced = !showAdvanced;
                            });
                          },
                          icon: Icon(
                            showAdvanced
                                ? Icons.unfold_less_outlined
                                : Icons.unfold_more_outlined,
                          ),
                          label: Text(
                            showAdvanced
                                ? 'Hide advanced fields'
                                : 'Show advanced fields',
                          ),
                        ),
                      ),
                      if (showAdvanced) ...[
                        const SizedBox(height: 8),
                        TextField(
                          controller: contentVersionController,
                          decoration: const InputDecoration(
                            labelText: 'contentVersion (optional)',
                            border: OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: 8),
                        TextField(
                          controller: sceneKeyController,
                          decoration: const InputDecoration(
                            labelText: 'sceneKey (optional)',
                            border: OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: 8),
                        TextField(
                          controller: entitlementKeyController,
                          decoration: const InputDecoration(
                            labelText: 'entitlementKey (optional)',
                            border: OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: 8),
                        TextField(
                          controller: thumbnailUrlController,
                          decoration: const InputDecoration(
                            labelText: 'thumbnailUrl',
                            border: OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: 8),
                        TextField(
                          controller: previewLinesController,
                          minLines: 2,
                          maxLines: 4,
                          decoration: const InputDecoration(
                            labelText: 'previewLines (one per line)',
                            border: OutlineInputBorder(),
                          ),
                        ),
                        const SizedBox(height: 8),
                        SwitchListTile(
                          value: requiresExplicitLicense,
                          onChanged: (value) {
                            setDialogState(() {
                              requiresExplicitLicense = value;
                            });
                          },
                          contentPadding: EdgeInsets.zero,
                          title: const Text('requiresExplicitLicense'),
                        ),
                        SwitchListTile(
                          value: supportsSaveResume,
                          onChanged: (value) {
                            setDialogState(() {
                              supportsSaveResume = value;
                            });
                          },
                          contentPadding: EdgeInsets.zero,
                          title: const Text('supportsSaveResume'),
                        ),
                      ],
                      if (validationError.trim().isNotEmpty) ...[
                        const SizedBox(height: 6),
                        Text(
                          validationError,
                          style: TextStyle(color: Colors.red.shade700),
                        ),
                      ],
                    ],
                  ),
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.of(dialogContext).pop(),
                  child: const Text('Cancel'),
                ),
                FilledButton.icon(
                  onPressed: () {
                    final normalizedGameId = gameIdController.text.trim();
                    final normalizedTargetVersion =
                        targetVersionController.text.trim();
                    final sortOrder =
                        int.tryParse(sortOrderController.text.trim());
                    if (normalizedGameId.isEmpty) {
                      setDialogState(() {
                        validationError = 'gameId is required.';
                      });
                      return;
                    }
                    if (normalizedTargetVersion.isEmpty) {
                      setDialogState(() {
                        validationError = 'targetContentVersion is required.';
                      });
                      return;
                    }
                    if (sortOrder == null) {
                      setDialogState(() {
                        validationError = 'sortOrder must be an integer.';
                      });
                      return;
                    }

                    final previewLines = previewLinesController.text
                        .split(RegExp(r'[\r\n]+'))
                        .map((line) => line.trim())
                        .where((line) => line.isNotEmpty)
                        .toList(growable: false);

                    final entry = AdminGameCatalogSeedEntry(
                      gameId: normalizedGameId,
                      title: titleController.text.trim(),
                      description: descriptionController.text.trim(),
                      targetContentVersion: normalizedTargetVersion,
                      contentVersion: contentVersionController.text.trim(),
                      sceneKey: sceneKeyController.text.trim(),
                      entitlementKey: entitlementKeyController.text.trim(),
                      deliveryMode: deliveryMode,
                      parameterSchema: existing?.parameterSchema,
                      packageUri: packageUriController.text.trim(),
                      thumbnailUrl: thumbnailUrlController.text.trim(),
                      supportsSaveResume: supportsSaveResume,
                      availableForPurchase: availableForPurchase,
                      requiresExplicitLicense: requiresExplicitLicense,
                      runtimeLaunchEnabled: runtimeLaunchEnabled,
                      sortOrder: sortOrder,
                      active: active,
                      previewLines: previewLines,
                      mobileControlSchema: existing?.mobileControlSchema,
                    );
                    Navigator.of(dialogContext).pop(entry);
                  },
                  icon: const Icon(Icons.save),
                  label: Text(isCreate ? 'Create' : 'Save'),
                ),
              ],
            );
          },
        );
      },
    );

    gameIdController.dispose();
    titleController.dispose();
    descriptionController.dispose();
    targetVersionController.dispose();
    contentVersionController.dispose();
    sceneKeyController.dispose();
    entitlementKeyController.dispose();
    packageUriController.dispose();
    thumbnailUrlController.dispose();
    sortOrderController.dispose();
    previewLinesController.dispose();

    return result;
  }

  Future<void> _createGameCatalogEntry() async {
    final entry = await _openGameCatalogEditorDialog();
    if (entry == null) {
      return;
    }

    final reason = _resolveReason('manual-game-catalog-create');
    final correlationId = _resolveCorrelationId();
    setState(() {
      _savingCatalogEntry = true;
    });
    try {
      await EntitlementAdminService.upsertGameCatalogEntry(
        entry: entry,
        reason: reason,
        correlationId: correlationId,
        action: 'CREATE_GAME_CATALOG_ENTRY',
      );
      _snack('Created game_catalog/${entry.gameId} ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Create game_catalog failed: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _savingCatalogEntry = false;
        });
      }
    }
  }

  Future<void> _editGameCatalogEntry(AdminGameCatalogSeedEntry existing) async {
    final entry = await _openGameCatalogEditorDialog(existing: existing);
    if (entry == null) {
      return;
    }

    final reason = _resolveReason('manual-game-catalog-edit');
    final correlationId = _resolveCorrelationId();
    setState(() {
      _savingCatalogEntry = true;
    });
    try {
      await EntitlementAdminService.upsertGameCatalogEntry(
        entry: entry,
        reason: reason,
        correlationId: correlationId,
        action: 'UPDATE_GAME_CATALOG_ENTRY',
      );
      _snack('Updated game_catalog/${entry.gameId} ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Update game_catalog failed: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _savingCatalogEntry = false;
        });
      }
    }
  }

  Future<bool> _confirmAction({
    required String title,
    required String message,
    required String confirmLabel,
    bool danger = false,
  }) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) {
        return AlertDialog(
          title: Text(title),
          content: Text(message),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(dialogContext).pop(false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              style: danger
                  ? FilledButton.styleFrom(
                      backgroundColor: Colors.red.shade700,
                    )
                  : null,
              onPressed: () => Navigator.of(dialogContext).pop(true),
              child: Text(confirmLabel),
            ),
          ],
        );
      },
    );
    return confirmed == true;
  }

  Future<void> _deactivateGameCatalogEntry(String gameId) async {
    final confirmed = await _confirmAction(
      title: 'Deactivate game',
      message: 'Set active=false and runtimeLaunchEnabled=false for "$gameId"?',
      confirmLabel: 'Deactivate',
    );
    if (!confirmed) {
      return;
    }

    final reason = _resolveReason('manual-game-catalog-deactivate');
    final correlationId = _resolveCorrelationId();
    setState(() {
      _savingCatalogEntry = true;
    });
    try {
      await EntitlementAdminService.deactivateGameCatalogEntry(
        gameId: gameId,
        reason: reason,
        correlationId: correlationId,
      );
      _snack('Deactivated game_catalog/$gameId ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Deactivate game_catalog failed: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _savingCatalogEntry = false;
        });
      }
    }
  }

  Future<void> _deleteGameCatalogEntry(String gameId) async {
    final confirmed = await _confirmAction(
      title: 'Delete game',
      message:
          'Permanently delete game_catalog/$gameId?\nUse deactivate if you only need to hide the game.',
      confirmLabel: 'Delete',
      danger: true,
    );
    if (!confirmed) {
      return;
    }

    final reason = _resolveReason('manual-game-catalog-delete');
    final correlationId = _resolveCorrelationId();
    setState(() {
      _savingCatalogEntry = true;
    });
    try {
      await EntitlementAdminService.deleteGameCatalogEntry(
        gameId: gameId,
        reason: reason,
        correlationId: correlationId,
      );
      _snack('Deleted game_catalog/$gameId ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Delete game_catalog failed: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _savingCatalogEntry = false;
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

  _SeedFreshnessStatus _evaluateSeedFreshness() {
    if (_loadingCatalogSeed || _loadingExportManifest) {
      return const _SeedFreshnessStatus(
        level: _SeedFreshnessLevel.loading,
        label: 'Loading',
        summary: 'Loading catalog seed and export manifest.',
        details: <String>[],
      );
    }

    if (_catalogSeedError.trim().isNotEmpty ||
        _exportManifestError.trim().isNotEmpty) {
      final details = <String>[
        if (_catalogSeedError.trim().isNotEmpty)
          'Catalog seed error: $_catalogSeedError',
        if (_exportManifestError.trim().isNotEmpty)
          'Export manifest error: $_exportManifestError',
      ];
      return _SeedFreshnessStatus(
        level: _SeedFreshnessLevel.error,
        label: 'Error',
        summary: 'Cannot validate authoring source freshness.',
        details: details,
      );
    }

    final manifest = _exportManifest;
    if (manifest == null) {
      return const _SeedFreshnessStatus(
        level: _SeedFreshnessLevel.error,
        label: 'Error',
        summary: 'Export manifest is unavailable.',
        details: <String>[],
      );
    }

    final catalogGameIds =
        _catalogSeedEntries.map((entry) => entry.gameId.toLowerCase()).toSet();
    final manifestGameIds =
        manifest.exportedGameIds.map((id) => id.toLowerCase()).toSet();
    final missingGameIds = manifestGameIds
        .where((id) => !catalogGameIds.contains(id))
        .toList()
      ..sort();
    final presentCount = manifestGameIds.length - missingGameIds.length;
    final age = DateTime.now().toUtc().difference(manifest.exportedAtUtc);
    final details = <String>[
      'ExportedAtUtc=${_formatUtc(manifest.exportedAtUtc)} (${_formatAge(age)})',
      'Manifest entries=${manifest.entries.length} (entryCount=${manifest.entryCount})',
      'Exported gameIds in seed=$presentCount/${manifestGameIds.length}',
    ];
    if (missingGameIds.isNotEmpty) {
      details.add('Missing in seed: ${missingGameIds.join(', ')}');
      return _SeedFreshnessStatus(
        level: _SeedFreshnessLevel.error,
        label: 'Error',
        summary: 'Seed is out of sync with export manifest.',
        details: details,
      );
    }

    if (age.isNegative) {
      return _SeedFreshnessStatus(
        level: _SeedFreshnessLevel.warning,
        label: 'Warning',
        summary: 'Manifest timestamp is in the future; check system clock.',
        details: details,
      );
    }

    if (age.inDays >= 7) {
      return _SeedFreshnessStatus(
        level: _SeedFreshnessLevel.stale,
        label: 'Stale',
        summary: 'Export manifest is older than 7 days.',
        details: details,
      );
    }

    if (age.inHours >= 72) {
      return _SeedFreshnessStatus(
        level: _SeedFreshnessLevel.warning,
        label: 'Warning',
        summary: 'Export manifest is older than 72 hours.',
        details: details,
      );
    }

    return _SeedFreshnessStatus(
      level: _SeedFreshnessLevel.healthy,
      label: 'Healthy',
      summary: 'Catalog seed is in sync with export manifest.',
      details: details,
    );
  }

  String _formatAge(Duration age) {
    if (age.isNegative) {
      return 'future by ${_formatDuration(-age)}';
    }
    return 'age ${_formatDuration(age)}';
  }

  String _formatDuration(Duration duration) {
    if (duration.inDays > 0) {
      return '${duration.inDays}d';
    }
    if (duration.inHours > 0) {
      return '${duration.inHours}h';
    }
    if (duration.inMinutes > 0) {
      return '${duration.inMinutes}m';
    }
    return '${duration.inSeconds}s';
  }

  Widget _buildSeedFreshnessCard() {
    final status = _evaluateSeedFreshness();
    final Color badgeBackground;
    final Color badgeForeground;
    switch (status.level) {
      case _SeedFreshnessLevel.healthy:
        badgeBackground = Colors.green.shade100;
        badgeForeground = Colors.green.shade900;
        break;
      case _SeedFreshnessLevel.warning:
        badgeBackground = Colors.orange.shade100;
        badgeForeground = Colors.orange.shade900;
        break;
      case _SeedFreshnessLevel.stale:
        badgeBackground = Colors.amber.shade100;
        badgeForeground = Colors.amber.shade900;
        break;
      case _SeedFreshnessLevel.error:
        badgeBackground = Colors.red.shade100;
        badgeForeground = Colors.red.shade900;
        break;
      case _SeedFreshnessLevel.loading:
        badgeBackground = Colors.blueGrey.shade100;
        badgeForeground = Colors.blueGrey.shade900;
        break;
    }

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: Colors.grey.shade100,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                decoration: BoxDecoration(
                  color: badgeBackground,
                  borderRadius: BorderRadius.circular(999),
                ),
                child: Text(
                  status.label,
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w600,
                    color: badgeForeground,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              const Text(
                'Seed Source Freshness',
                style: TextStyle(fontWeight: FontWeight.w600),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            status.summary,
            style: TextStyle(
              fontSize: 12,
              color: status.level == _SeedFreshnessLevel.error
                  ? Colors.red.shade700
                  : null,
            ),
          ),
          if (status.details.isNotEmpty) ...[
            const SizedBox(height: 6),
            for (final detail in status.details)
              Text(
                detail,
                style: const TextStyle(fontSize: 12),
              ),
          ],
        ],
      ),
    );
  }

  void _useTargetUid(String uid) {
    final normalizedUid = uid.trim();
    if (normalizedUid.isEmpty) {
      return;
    }

    _targetUserIdController.text = normalizedUid;
    _snack('Ustawiono Target UID: $normalizedUid');
  }

  Future<_AccountCreateRequest?> _openAccountCreateDialog() async {
    final emailController = TextEditingController();
    final passwordController = TextEditingController();
    final confirmPasswordController = TextEditingController();
    final expiresDaysController = TextEditingController(text: '365');
    var role = EntitlementRole.therapist;
    var planTier = SubscriptionPlanTier.basic;
    var appStatus = LicenseStatus.active;
    var perpetual = true;
    var validationError = '';

    final result = await showDialog<_AccountCreateRequest>(
      context: context,
      builder: (dialogContext) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            return AlertDialog(
              title: const Text('Create therapist/parent account'),
              content: SizedBox(
                width: 520,
                child: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const Text(
                        'Creates Firebase Auth account (email + password) and writes user_entitlements for generated UID.',
                        style: TextStyle(fontSize: 12),
                      ),
                      const SizedBox(height: 8),
                      TextField(
                        controller: emailController,
                        keyboardType: TextInputType.emailAddress,
                        decoration: const InputDecoration(
                          labelText: 'Email',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      TextField(
                        controller: passwordController,
                        obscureText: true,
                        decoration: const InputDecoration(
                          labelText: 'Password',
                          helperText: 'Minimum 6 characters.',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      TextField(
                        controller: confirmPasswordController,
                        obscureText: true,
                        decoration: const InputDecoration(
                          labelText: 'Confirm password',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      DropdownButtonFormField<EntitlementRole>(
                        initialValue: role,
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
                          if (value == null) {
                            return;
                          }
                          setDialogState(() {
                            role = value;
                            if (role == EntitlementRole.parent &&
                                planTier == SubscriptionPlanTier.basic) {
                              planTier = SubscriptionPlanTier.free;
                            } else if (role == EntitlementRole.therapist &&
                                planTier == SubscriptionPlanTier.free) {
                              planTier = SubscriptionPlanTier.basic;
                            }
                          });
                        },
                      ),
                      const SizedBox(height: 8),
                      DropdownButtonFormField<SubscriptionPlanTier>(
                        initialValue: planTier,
                        decoration: const InputDecoration(
                          labelText: 'Plan tier',
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
                          if (value == null) {
                            return;
                          }
                          setDialogState(() {
                            planTier = value;
                          });
                        },
                      ),
                      const SizedBox(height: 8),
                      DropdownButtonFormField<LicenseStatus>(
                        initialValue: appStatus,
                        decoration: const InputDecoration(
                          labelText: 'App license status',
                          border: OutlineInputBorder(),
                        ),
                        items: const [
                          DropdownMenuItem(
                            value: LicenseStatus.active,
                            child: Text('ACTIVE'),
                          ),
                          DropdownMenuItem(
                            value: LicenseStatus.expired,
                            child: Text('EXPIRED'),
                          ),
                          DropdownMenuItem(
                            value: LicenseStatus.revoked,
                            child: Text('REVOKED'),
                          ),
                          DropdownMenuItem(
                            value: LicenseStatus.none,
                            child: Text('NONE'),
                          ),
                        ],
                        onChanged: (value) {
                          if (value == null) {
                            return;
                          }
                          setDialogState(() {
                            appStatus = value;
                          });
                        },
                      ),
                      const SizedBox(height: 8),
                      SwitchListTile(
                        value: perpetual,
                        onChanged: (value) {
                          setDialogState(() {
                            perpetual = value;
                          });
                        },
                        contentPadding: EdgeInsets.zero,
                        title: const Text('Perpetual app license'),
                      ),
                      if (!perpetual)
                        TextField(
                          controller: expiresDaysController,
                          keyboardType: TextInputType.number,
                          decoration: const InputDecoration(
                            labelText: 'Expires in days',
                            border: OutlineInputBorder(),
                          ),
                        ),
                      if (validationError.trim().isNotEmpty) ...[
                        const SizedBox(height: 6),
                        Text(
                          validationError,
                          style: TextStyle(color: Colors.red.shade700),
                        ),
                      ],
                    ],
                  ),
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.of(dialogContext).pop(),
                  child: const Text('Cancel'),
                ),
                FilledButton.icon(
                  onPressed: () {
                    final email = emailController.text.trim().toLowerCase();
                    final password = passwordController.text.trim();
                    final confirmPassword =
                        confirmPasswordController.text.trim();
                    final expiresInDays =
                        int.tryParse(expiresDaysController.text.trim()) ?? 365;
                    if (email.isEmpty) {
                      setDialogState(() {
                        validationError = 'Email is required.';
                      });
                      return;
                    }
                    if (!email.contains('@') || email.startsWith('@')) {
                      setDialogState(() {
                        validationError = 'Provide a valid email address.';
                      });
                      return;
                    }
                    if (password.length < 6) {
                      setDialogState(() {
                        validationError =
                            'Password must have at least 6 characters.';
                      });
                      return;
                    }
                    if (password != confirmPassword) {
                      setDialogState(() {
                        validationError =
                            'Password and confirmation must match.';
                      });
                      return;
                    }
                    if (!perpetual && expiresInDays <= 0) {
                      setDialogState(() {
                        validationError = 'Expires in days must be > 0.';
                      });
                      return;
                    }
                    Navigator.of(dialogContext).pop(
                      _AccountCreateRequest(
                        email: email,
                        password: password,
                        role: role,
                        planTier: planTier,
                        appStatus: appStatus,
                        perpetual: perpetual,
                        expiresInDays: expiresInDays,
                      ),
                    );
                  },
                  icon: const Icon(Icons.person_add),
                  label: const Text('Create'),
                ),
              ],
            );
          },
        );
      },
    );

    emailController.dispose();
    passwordController.dispose();
    confirmPasswordController.dispose();
    expiresDaysController.dispose();
    return result;
  }

  Future<void> _createDirectoryAccount() async {
    final request = await _openAccountCreateDialog();
    if (request == null) {
      return;
    }

    final reason = _resolveReason('manual-account-create');
    final correlationId = _resolveCorrelationId();
    setState(() {
      _savingAccountDirectory = true;
    });
    String createdUserId = '';
    try {
      createdUserId = await EntitlementAdminService.createAuthUserAccount(
        email: request.email,
        password: request.password,
        reason: reason,
        correlationId: correlationId,
      );
      await EntitlementAdminService.upsertUserEntitlement(
        userId: createdUserId,
        role: request.role,
        appLicense: _buildGrant(
          status: request.appStatus,
          perpetual: request.perpetual,
          days: request.expiresInDays,
        ),
        planProfile: EntitlementPlanProfile.fromMap(
          <String, dynamic>{
            'tier': request.planTier.wireValue,
          },
        ),
        reason: reason,
        correlationId: correlationId,
      );
      _useTargetUid(createdUserId);
      _snack(
        'Created ${request.email} (uid=$createdUserId) + entitlement ($correlationId)',
      );
      _rotateCorrelationId();
    } catch (e) {
      if (createdUserId.isNotEmpty) {
        _snack(
          'Auth user uid=$createdUserId created, but entitlement write failed: $e',
          error: true,
        );
      } else {
        _snack('Create account failed: $e', error: true);
      }
    } finally {
      if (mounted) {
        setState(() {
          _savingAccountDirectory = false;
        });
      }
    }
  }

  void _openDirectoryUserInOperations(AdminDirectoryUserRow user) {
    setState(() {
      _targetUserIdController.text = user.userId;
      _selectedRole = user.role;
      _selectedPlanTier = user.planTier;
      _selectedAppLicenseStatus = user.appLicenseStatus;
      _entitlementPerpetual = true;
    });
    DefaultTabController.of(context).animateTo(0);
    _snack('Loaded ${user.userId} into Operations editor');
  }

  Future<void> _deleteDirectoryAccount(String userId) async {
    final confirmed = await _confirmAction(
      title: 'Delete entitlement',
      message: 'Delete user_entitlements/$userId?',
      confirmLabel: 'Delete',
      danger: true,
    );
    if (!confirmed) {
      return;
    }

    final reason = _resolveReason('manual-account-delete');
    final correlationId = _resolveCorrelationId();
    setState(() {
      _savingAccountDirectory = true;
    });
    try {
      await EntitlementAdminService.deleteUserEntitlement(
        userId: userId,
        reason: reason,
        correlationId: correlationId,
      );
      _snack('Deleted user_entitlements/$userId ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Delete account failed: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _savingAccountDirectory = false;
        });
      }
    }
  }

  Future<List<AdminDirectoryUserRow>> _loadTherapistDirectory() async {
    final therapists = await EntitlementAdminService.watchUsersByRole(
      EntitlementRole.therapist,
    ).first;
    final sorted = List<AdminDirectoryUserRow>.from(therapists, growable: false)
      ..sort((left, right) => left.userId.compareTo(right.userId));
    return sorted;
  }

  Future<_StudentEditorResult?> _openStudentEditorDialog({
    required List<AdminDirectoryUserRow> therapists,
    AdminStudentDirectoryRow? existing,
  }) async {
    final isCreate = existing == null;
    final studentIdController =
        TextEditingController(text: existing?.studentId ?? '');
    final firstNameController =
        TextEditingController(text: existing?.firstName ?? '');
    final lastNameController =
        TextEditingController(text: existing?.lastName ?? '');
    final therapistIds = therapists
        .map((row) => row.userId.trim())
        .where((uid) => uid.isNotEmpty)
        .toSet()
        .toList(growable: true)
      ..sort();
    final existingTherapistId = existing?.therapistId.trim() ?? '';
    if (existingTherapistId.isNotEmpty &&
        !therapistIds.contains(existingTherapistId)) {
      therapistIds.insert(0, existingTherapistId);
    }
    var selectedTherapistId = existingTherapistId.isNotEmpty
        ? existingTherapistId
        : (therapistIds.isNotEmpty ? therapistIds.first : '');
    var validationError = '';

    final result = await showDialog<_StudentEditorResult>(
      context: context,
      builder: (dialogContext) {
        return StatefulBuilder(
          builder: (context, setDialogState) {
            return AlertDialog(
              title: Text(isCreate ? 'Create student' : 'Edit student'),
              content: SizedBox(
                width: 480,
                child: SingleChildScrollView(
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      TextField(
                        controller: studentIdController,
                        enabled: isCreate,
                        decoration: const InputDecoration(
                          labelText: 'studentId (optional when creating)',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      if (therapistIds.isEmpty)
                        Text(
                          'No therapists found. Create therapist account first.',
                          style: TextStyle(color: Colors.red.shade700),
                        )
                      else
                        DropdownButtonFormField<String>(
                          initialValue: selectedTherapistId.isEmpty
                              ? null
                              : selectedTherapistId,
                          decoration: const InputDecoration(
                            labelText: 'Owner therapist UID',
                            border: OutlineInputBorder(),
                          ),
                          items: [
                            for (final therapistId in therapistIds)
                              DropdownMenuItem<String>(
                                value: therapistId,
                                child: Text(therapistId),
                              ),
                          ],
                          onChanged: (value) {
                            setDialogState(() {
                              selectedTherapistId = value?.trim() ?? '';
                            });
                          },
                        ),
                      const SizedBox(height: 8),
                      TextField(
                        controller: firstNameController,
                        decoration: const InputDecoration(
                          labelText: 'firstName',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      const SizedBox(height: 8),
                      TextField(
                        controller: lastNameController,
                        decoration: const InputDecoration(
                          labelText: 'lastName',
                          border: OutlineInputBorder(),
                        ),
                      ),
                      if (validationError.trim().isNotEmpty) ...[
                        const SizedBox(height: 6),
                        Text(
                          validationError,
                          style: TextStyle(color: Colors.red.shade700),
                        ),
                      ],
                    ],
                  ),
                ),
              ),
              actions: [
                TextButton(
                  onPressed: () => Navigator.of(dialogContext).pop(),
                  child: const Text('Cancel'),
                ),
                FilledButton.icon(
                  onPressed: () {
                    final therapistId = selectedTherapistId.trim();
                    if (therapistId.isEmpty) {
                      setDialogState(() {
                        validationError = 'therapistId is required.';
                      });
                      return;
                    }
                    Navigator.of(dialogContext).pop(
                      _StudentEditorResult(
                        studentId: studentIdController.text.trim(),
                        therapistId: therapistId,
                        firstName: firstNameController.text.trim(),
                        lastName: lastNameController.text.trim(),
                      ),
                    );
                  },
                  icon: const Icon(Icons.save),
                  label: Text(isCreate ? 'Create' : 'Save'),
                ),
              ],
            );
          },
        );
      },
    );

    studentIdController.dispose();
    firstNameController.dispose();
    lastNameController.dispose();
    return result;
  }

  Future<void> _createStudentRecord() async {
    List<AdminDirectoryUserRow> therapists;
    try {
      therapists = await _loadTherapistDirectory();
    } catch (e) {
      _snack('Load therapist list failed: $e', error: true);
      return;
    }
    final request = await _openStudentEditorDialog(therapists: therapists);
    if (request == null) {
      return;
    }

    final reason = _resolveReason('manual-student-create');
    final correlationId = _resolveCorrelationId();
    setState(() {
      _savingStudentDirectory = true;
    });
    try {
      final studentId = await EntitlementAdminService.upsertStudentRecord(
        studentId: request.studentId,
        therapistId: request.therapistId,
        firstName: request.firstName,
        lastName: request.lastName,
        reason: reason,
        correlationId: correlationId,
        action: 'CREATE_STUDENT_DIRECTORY_ENTRY',
      );
      _snack('Created students/$studentId ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Create student failed: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _savingStudentDirectory = false;
        });
      }
    }
  }

  Future<void> _editStudentRecord(AdminStudentDirectoryRow student) async {
    List<AdminDirectoryUserRow> therapists;
    try {
      therapists = await _loadTherapistDirectory();
    } catch (e) {
      _snack('Load therapist list failed: $e', error: true);
      return;
    }
    final request = await _openStudentEditorDialog(
      therapists: therapists,
      existing: student,
    );
    if (request == null) {
      return;
    }

    final reason = _resolveReason('manual-student-edit');
    final correlationId = _resolveCorrelationId();
    setState(() {
      _savingStudentDirectory = true;
    });
    try {
      final studentId = await EntitlementAdminService.upsertStudentRecord(
        studentId: student.studentId,
        therapistId: request.therapistId,
        firstName: request.firstName,
        lastName: request.lastName,
        reason: reason,
        correlationId: correlationId,
        action: 'UPDATE_STUDENT_DIRECTORY_ENTRY',
      );
      _snack('Updated students/$studentId ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Update student failed: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _savingStudentDirectory = false;
        });
      }
    }
  }

  Future<void> _deleteStudentRecord(String studentId) async {
    final confirmed = await _confirmAction(
      title: 'Delete student',
      message: 'Delete students/$studentId?',
      confirmLabel: 'Delete',
      danger: true,
    );
    if (!confirmed) {
      return;
    }

    final reason = _resolveReason('manual-student-delete');
    final correlationId = _resolveCorrelationId();
    setState(() {
      _savingStudentDirectory = true;
    });
    try {
      await EntitlementAdminService.deleteStudentRecord(
        studentId: studentId,
        reason: reason,
        correlationId: correlationId,
      );
      _snack('Deleted students/$studentId ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Delete student failed: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _savingStudentDirectory = false;
        });
      }
    }
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

  String _normalizedLower(String value) {
    return value.trim().toLowerCase();
  }

  String _anonymizedToken(String raw) {
    final value = raw.trim();
    if (value.isEmpty) {
      return '(empty)';
    }
    var hash = 0x811c9dc5;
    for (final codeUnit in value.codeUnits) {
      hash ^= codeUnit;
      hash = (hash * 0x01000193) & 0xffffffff;
    }
    final suffix = hash.toUnsigned(32).toRadixString(16).padLeft(8, '0');
    return 'anon_$suffix';
  }

  String _personDisplayLabel({
    required String id,
    required Map<String, String> namesById,
    required String unknownLabel,
  }) {
    final normalizedId = id.trim();
    if (normalizedId.isEmpty) {
      return unknownLabel;
    }
    if (_anonymizeSessionData) {
      return _anonymizedToken(normalizedId);
    }

    final name = namesById[normalizedId]?.trim() ?? '';
    if (name.isNotEmpty && name != normalizedId) {
      return name;
    }
    return normalizedId;
  }

  String _personDropdownLabel({
    required String id,
    required Map<String, String> namesById,
  }) {
    final normalizedId = id.trim();
    if (normalizedId.isEmpty) {
      return '(empty)';
    }
    if (_anonymizeSessionData) {
      return _anonymizedToken(normalizedId);
    }

    final name = namesById[normalizedId]?.trim() ?? '';
    if (name.isNotEmpty && name != normalizedId) {
      return '$name · $normalizedId';
    }
    return normalizedId;
  }

  bool _matchesSessionStateFilter(AdminTherapySessionRow session) {
    switch (_sessionStateFilter) {
      case _SessionStateFilter.all:
        return true;
      case _SessionStateFilter.active:
        return !session.isTerminal;
      case _SessionStateFilter.terminal:
        return session.isTerminal;
      case _SessionStateFilter.interrupted:
        return session.stateLabel == 'INTERRUPTED';
    }
  }

  String _sessionStateFilterLabel(_SessionStateFilter filter) {
    switch (filter) {
      case _SessionStateFilter.all:
        return 'All';
      case _SessionStateFilter.active:
        return 'Active';
      case _SessionStateFilter.terminal:
        return 'Terminal';
      case _SessionStateFilter.interrupted:
        return 'Interrupted';
    }
  }

  void _clearSessionFilters() {
    setState(() {
      _sessionTherapistFilter = '';
      _sessionStudentFilter = '';
      _sessionIdFilterController.clear();
      _sessionStateFilter = _SessionStateFilter.all;
      _selectedSessionDocumentId = '';
      _selectedSessionDocumentIds.clear();
    });
  }

  void _selectSessionForEvents(AdminTherapySessionRow session) {
    final normalizedDocumentId = session.documentId.trim();
    if (normalizedDocumentId.isEmpty) {
      return;
    }
    setState(() {
      _selectedSessionDocumentId = normalizedDocumentId;
    });
  }

  void _toggleSessionSelection(String sessionDocumentId) {
    final normalized = sessionDocumentId.trim();
    if (normalized.isEmpty) {
      return;
    }
    setState(() {
      if (_selectedSessionDocumentIds.contains(normalized)) {
        _selectedSessionDocumentIds.remove(normalized);
      } else {
        _selectedSessionDocumentIds.add(normalized);
      }
    });
  }

  void _selectVisibleSessions(List<AdminTherapySessionRow> sessions) {
    final ids = sessions
        .map((session) => session.documentId.trim())
        .where((id) => id.isNotEmpty)
        .toSet();
    if (ids.isEmpty) {
      return;
    }
    setState(() {
      _selectedSessionDocumentIds.addAll(ids);
    });
  }

  void _clearSelectedSessions() {
    setState(() {
      _selectedSessionDocumentIds.clear();
    });
  }

  Future<void> _deleteSelectedSessions() async {
    if (_selectedSessionDocumentIds.isEmpty) {
      return;
    }

    final selectedIds = _selectedSessionDocumentIds.toList(growable: false)
      ..sort();
    final confirmed = await _confirmAction(
      title: 'Delete selected sessions',
      message:
          'Delete ${selectedIds.length} selected therapy_sessions documents?\n\n'
          'Note: nested events subcollection documents are retained by current Firestore policy and become inaccessible after session delete.',
      confirmLabel: 'Delete selected',
      danger: true,
    );
    if (!confirmed) {
      return;
    }

    final reason = _resolveReason('manual-session-delete');
    final correlationId = _resolveCorrelationId();
    setState(() {
      _deletingSessions = true;
    });
    try {
      final deletedCount = await EntitlementAdminService.deleteTherapySessions(
        sessionDocumentIds: selectedIds,
        reason: reason,
        correlationId: correlationId,
      );

      setState(() {
        _selectedSessionDocumentIds.clear();
        if (selectedIds.contains(_selectedSessionDocumentId)) {
          _selectedSessionDocumentId = '';
        }
      });
      _snack('Deleted $deletedCount therapy_sessions docs ($correlationId)');
      _rotateCorrelationId();
    } catch (e) {
      _snack('Delete selected sessions failed: $e', error: true);
    } finally {
      if (mounted) {
        setState(() {
          _deletingSessions = false;
        });
      }
    }
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
        _buildAccountsCrudCard(),
        const SizedBox(height: 12),
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

  Widget _buildAccountsCrudCard() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Account CRUD',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            const Text(
              'Create Firebase Auth login (email/password) and entitlement profile. Delete removes entitlement record.',
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                FilledButton.icon(
                  onPressed:
                      _savingAccountDirectory ? null : _createDirectoryAccount,
                  icon: _savingAccountDirectory
                      ? const SizedBox(
                          width: 14,
                          height: 14,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.person_add),
                  label: const Text('Create account (Auth + entitlement)'),
                ),
                OutlinedButton.icon(
                  onPressed: _useCurrentUserUidAsTarget,
                  icon: const Icon(Icons.person_pin),
                  label: const Text('Use my UID'),
                ),
              ],
            ),
          ],
        ),
      ),
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
                            Column(
                              crossAxisAlignment: CrossAxisAlignment.end,
                              children: [
                                TextButton(
                                  onPressed: () => _useTargetUid(user.userId),
                                  child: const Text('Use UID'),
                                ),
                                TextButton(
                                  onPressed: () =>
                                      _openDirectoryUserInOperations(user),
                                  child: const Text('Edit entitlement'),
                                ),
                                TextButton(
                                  onPressed: _savingAccountDirectory
                                      ? null
                                      : () => _deleteDirectoryAccount(
                                            user.userId,
                                          ),
                                  child: Text(
                                    'Delete',
                                    style: TextStyle(
                                      color: Colors.red.shade700,
                                    ),
                                  ),
                                ),
                              ],
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
                const Text(
                  'Manage students collection records. Therapist owner is selected from therapist directory list.',
                ),
                const SizedBox(height: 8),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    FilledButton.icon(
                      onPressed:
                          _savingStudentDirectory ? null : _createStudentRecord,
                      icon: _savingStudentDirectory
                          ? const SizedBox(
                              width: 14,
                              height: 14,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.person_add_alt_1),
                      label: const Text('Create student'),
                    ),
                    OutlinedButton.icon(
                      onPressed: _useCurrentUserUidAsTarget,
                      icon: const Icon(Icons.person_pin),
                      label: const Text('Use my UID'),
                    ),
                  ],
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
                                Column(
                                  crossAxisAlignment: CrossAxisAlignment.end,
                                  children: [
                                    TextButton(
                                      onPressed: () =>
                                          _useTargetUid(student.therapistId),
                                      child: const Text('Use owner UID'),
                                    ),
                                    TextButton(
                                      onPressed: _savingStudentDirectory
                                          ? null
                                          : () => _editStudentRecord(student),
                                      child: const Text('Edit'),
                                    ),
                                    TextButton(
                                      onPressed: _savingStudentDirectory
                                          ? null
                                          : () => _deleteStudentRecord(
                                                student.studentId,
                                              ),
                                      child: Text(
                                        'Delete',
                                        style: TextStyle(
                                          color: Colors.red.shade700,
                                        ),
                                      ),
                                    ),
                                  ],
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

  Widget _buildSessionsTab() {
    return StreamBuilder<List<AdminTherapySessionRow>>(
      stream: EntitlementAdminService.watchRecentTherapySessions(
        limit: 300,
      ),
      builder: (context, snapshot) {
        if (snapshot.hasError) {
          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Card(
                child: Padding(
                  padding: const EdgeInsets.all(12),
                  child: Text(
                    'Blad odczytu therapy_sessions: ${snapshot.error}',
                    style: TextStyle(color: Colors.red.shade700),
                  ),
                ),
              ),
            ],
          );
        }

        if (!snapshot.hasData) {
          return const Center(child: CircularProgressIndicator());
        }

        final sessions = snapshot.data!;
        return StreamBuilder<List<AdminDirectoryUserRow>>(
          stream: EntitlementAdminService.watchUsersByRole(
            EntitlementRole.therapist,
          ),
          builder: (context, therapistSnapshot) {
            final therapists =
                therapistSnapshot.data ?? const <AdminDirectoryUserRow>[];
            final therapistNamesById = <String, String>{
              for (final therapist in therapists)
                therapist.userId.trim(): therapist.displayName,
            };

            return StreamBuilder<List<AdminStudentDirectoryRow>>(
              stream: EntitlementAdminService.watchStudents(limit: 600),
              builder: (context, studentSnapshot) {
                final students =
                    studentSnapshot.data ?? const <AdminStudentDirectoryRow>[];
                final studentNamesById = <String, String>{
                  for (final student in students)
                    student.studentId.trim(): student.fullName,
                };

                final therapistValues = <String>{
                  for (final session in sessions)
                    if (session.therapistId.trim().isNotEmpty)
                      session.therapistId.trim(),
                }.toList(growable: false)
                  ..sort((left, right) => _personDropdownLabel(
                        id: left,
                        namesById: therapistNamesById,
                      ).toLowerCase().compareTo(
                            _personDropdownLabel(
                              id: right,
                              namesById: therapistNamesById,
                            ).toLowerCase(),
                          ));
                final studentValues = <String>{
                  for (final session in sessions)
                    if (session.studentId.trim().isNotEmpty)
                      session.studentId.trim(),
                }.toList(growable: false)
                  ..sort((left, right) => _personDropdownLabel(
                        id: left,
                        namesById: studentNamesById,
                      ).toLowerCase().compareTo(
                            _personDropdownLabel(
                              id: right,
                              namesById: studentNamesById,
                            ).toLowerCase(),
                          ));

                final effectiveTherapistFilter =
                    therapistValues.contains(_sessionTherapistFilter)
                        ? _sessionTherapistFilter
                        : '';
                final effectiveStudentFilter =
                    studentValues.contains(_sessionStudentFilter)
                        ? _sessionStudentFilter
                        : '';

                final filteredSessions = sessions.where((session) {
                  final therapistFilter =
                      _normalizedLower(effectiveTherapistFilter);
                  final studentFilter =
                      _normalizedLower(effectiveStudentFilter);
                  final sessionFilter =
                      _normalizedLower(_sessionIdFilterController.text);
                  if (therapistFilter.isNotEmpty &&
                      !_normalizedLower(session.therapistId)
                          .contains(therapistFilter)) {
                    return false;
                  }
                  if (studentFilter.isNotEmpty &&
                      !_normalizedLower(session.studentId)
                          .contains(studentFilter)) {
                    return false;
                  }
                  if (sessionFilter.isNotEmpty &&
                      !_normalizedLower(session.sessionId)
                          .contains(sessionFilter)) {
                    return false;
                  }
                  return _matchesSessionStateFilter(session);
                }).toList(growable: false);

                final allSessionIds = sessions
                    .map((session) => session.documentId.trim())
                    .where((id) => id.isNotEmpty)
                    .toSet();
                final staleSelectedIds = _selectedSessionDocumentIds
                    .where((id) => !allSessionIds.contains(id))
                    .toList(growable: false);
                if (staleSelectedIds.isNotEmpty ||
                    (_selectedSessionDocumentId.isNotEmpty &&
                        !allSessionIds.contains(_selectedSessionDocumentId))) {
                  WidgetsBinding.instance.addPostFrameCallback((_) {
                    if (!mounted) {
                      return;
                    }
                    setState(() {
                      _selectedSessionDocumentIds.removeAll(staleSelectedIds);
                      if (!allSessionIds.contains(_selectedSessionDocumentId)) {
                        _selectedSessionDocumentId = '';
                      }
                    });
                  });
                }

                final selectedVisibleCount = filteredSessions
                    .where(
                      (session) => _selectedSessionDocumentIds
                          .contains(session.documentId.trim()),
                    )
                    .length;

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
                              'Session Results',
                              style: TextStyle(fontWeight: FontWeight.w700),
                            ),
                            const SizedBox(height: 8),
                            const Text(
                              'Read-only view of therapy_sessions and timeline events.',
                            ),
                            if (therapistSnapshot.hasError ||
                                studentSnapshot.hasError) ...[
                              const SizedBox(height: 8),
                              Text(
                                'Directory names partially unavailable. Fallback to IDs.',
                                style: TextStyle(color: Colors.red.shade700),
                              ),
                            ],
                            const SizedBox(height: 10),
                            SwitchListTile(
                              value: _anonymizeSessionData,
                              onChanged: (value) {
                                setState(() {
                                  _anonymizeSessionData = value;
                                });
                              },
                              contentPadding: EdgeInsets.zero,
                              title: const Text('Anonymize identifiers'),
                              subtitle: Text(
                                _anonymizeSessionData
                                    ? 'Showing hashed labels only.'
                                    : 'Showing therapist/student names when available.',
                              ),
                            ),
                            const SizedBox(height: 8),
                            DropdownButtonFormField<String>(
                              key: ValueKey<String>(
                                'session-therapist-$effectiveTherapistFilter-$_anonymizeSessionData',
                              ),
                              initialValue: effectiveTherapistFilter,
                              decoration: const InputDecoration(
                                labelText: 'Therapist filter',
                                border: OutlineInputBorder(),
                              ),
                              items: [
                                const DropdownMenuItem<String>(
                                  value: '',
                                  child: Text('All therapists'),
                                ),
                                for (final value in therapistValues)
                                  DropdownMenuItem<String>(
                                    value: value,
                                    child: Text(
                                      _personDropdownLabel(
                                        id: value,
                                        namesById: therapistNamesById,
                                      ),
                                    ),
                                  ),
                              ],
                              onChanged: (value) {
                                setState(() {
                                  _sessionTherapistFilter = value?.trim() ?? '';
                                });
                              },
                            ),
                            const SizedBox(height: 8),
                            DropdownButtonFormField<String>(
                              key: ValueKey<String>(
                                'session-student-$effectiveStudentFilter-$_anonymizeSessionData',
                              ),
                              initialValue: effectiveStudentFilter,
                              decoration: const InputDecoration(
                                labelText: 'Student filter',
                                border: OutlineInputBorder(),
                              ),
                              items: [
                                const DropdownMenuItem<String>(
                                  value: '',
                                  child: Text('All students'),
                                ),
                                for (final value in studentValues)
                                  DropdownMenuItem<String>(
                                    value: value,
                                    child: Text(
                                      _personDropdownLabel(
                                        id: value,
                                        namesById: studentNamesById,
                                      ),
                                    ),
                                  ),
                              ],
                              onChanged: (value) {
                                setState(() {
                                  _sessionStudentFilter = value?.trim() ?? '';
                                });
                              },
                            ),
                            const SizedBox(height: 8),
                            TextField(
                              controller: _sessionIdFilterController,
                              decoration: const InputDecoration(
                                labelText: 'Session ID contains',
                                border: OutlineInputBorder(),
                              ),
                            ),
                            const SizedBox(height: 8),
                            Wrap(
                              spacing: 8,
                              runSpacing: 8,
                              children: [
                                for (final filter in _SessionStateFilter.values)
                                  ChoiceChip(
                                    selected: _sessionStateFilter == filter,
                                    label:
                                        Text(_sessionStateFilterLabel(filter)),
                                    onSelected: (_) {
                                      setState(() {
                                        _sessionStateFilter = filter;
                                      });
                                    },
                                  ),
                              ],
                            ),
                            const SizedBox(height: 8),
                            Wrap(
                              spacing: 8,
                              runSpacing: 8,
                              children: [
                                OutlinedButton.icon(
                                  onPressed: _clearSessionFilters,
                                  icon: const Icon(
                                    Icons.filter_alt_off_outlined,
                                  ),
                                  label: const Text('Clear filters'),
                                ),
                                if (_selectedSessionDocumentId.isNotEmpty)
                                  OutlinedButton.icon(
                                    onPressed: () {
                                      setState(() {
                                        _selectedSessionDocumentId = '';
                                      });
                                    },
                                    icon: const Icon(
                                        Icons.visibility_off_outlined),
                                    label: const Text('Hide events'),
                                  ),
                                OutlinedButton.icon(
                                  onPressed: filteredSessions.isEmpty
                                      ? null
                                      : () => _selectVisibleSessions(
                                          filteredSessions),
                                  icon: const Icon(Icons.select_all),
                                  label: const Text('Select visible'),
                                ),
                                OutlinedButton.icon(
                                  onPressed: _selectedSessionDocumentIds.isEmpty
                                      ? null
                                      : _clearSelectedSessions,
                                  icon: const Icon(Icons.remove_done_outlined),
                                  label: const Text('Clear selected'),
                                ),
                                FilledButton.icon(
                                  onPressed: _deletingSessions ||
                                          _selectedSessionDocumentIds.isEmpty
                                      ? null
                                      : _deleteSelectedSessions,
                                  icon: _deletingSessions
                                      ? const SizedBox(
                                          width: 14,
                                          height: 14,
                                          child: CircularProgressIndicator(
                                            strokeWidth: 2,
                                          ),
                                        )
                                      : const Icon(Icons.delete_outline),
                                  label: Text(
                                    'Delete selected (${_selectedSessionDocumentIds.length})',
                                  ),
                                ),
                              ],
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                    Card(
                      child: Padding(
                        padding: const EdgeInsets.all(12),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              'Visible sessions: ${filteredSessions.length}/${sessions.length}',
                            ),
                            Text(
                              'Selected: ${_selectedSessionDocumentIds.length} total ($selectedVisibleCount visible)',
                              style: const TextStyle(fontSize: 12),
                            ),
                            const SizedBox(height: 8),
                            if (filteredSessions.isEmpty)
                              const Text('Brak sesji pasujacych do filtrow.')
                            else
                              for (final session in filteredSessions)
                                _buildSessionRow(
                                  session: session,
                                  therapistNamesById: therapistNamesById,
                                  studentNamesById: studentNamesById,
                                ),
                          ],
                        ),
                      ),
                    ),
                  ],
                );
              },
            );
          },
        );
      },
    );
  }

  Widget _buildSessionRow({
    required AdminTherapySessionRow session,
    required Map<String, String> therapistNamesById,
    required Map<String, String> studentNamesById,
  }) {
    final normalizedSessionDocumentId = session.documentId.trim();
    final isChecked =
        _selectedSessionDocumentIds.contains(normalizedSessionDocumentId);
    final sessionTitle = _anonymizeSessionData
        ? _anonymizedToken(session.sessionId)
        : session.sessionId;
    final therapistLabel = _personDisplayLabel(
      id: session.therapistId,
      namesById: therapistNamesById,
      unknownLabel: '(unknown therapist)',
    );
    final studentLabel = _personDisplayLabel(
      id: session.studentId,
      namesById: studentNamesById,
      unknownLabel: '(unknown student)',
    );
    final docIdLabel = _anonymizeSessionData
        ? _anonymizedToken(normalizedSessionDocumentId)
        : normalizedSessionDocumentId;

    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(8),
      decoration: BoxDecoration(
        color: Colors.grey.shade100,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Checkbox(
                value: isChecked,
                onChanged: _deletingSessions
                    ? null
                    : (_) =>
                        _toggleSessionSelection(normalizedSessionDocumentId),
              ),
              Expanded(
                child: Text(
                  sessionTitle,
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
              ),
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 8,
                  vertical: 4,
                ),
                decoration: BoxDecoration(
                  color: session.isTerminal
                      ? Colors.red.shade100
                      : Colors.green.shade100,
                  borderRadius: BorderRadius.circular(999),
                ),
                child: Text(
                  session.stateLabel,
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w700,
                    color: session.isTerminal
                        ? Colors.red.shade800
                        : Colors.green.shade800,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(
            'Therapist=$therapistLabel | Student=$studentLabel',
            style: const TextStyle(fontSize: 12),
          ),
          Text(
            'Reason=${session.reasonCode.isEmpty ? '-' : session.reasonCode} | '
            'Game=${session.latestGameId.isEmpty ? '-' : session.latestGameId}',
            style: const TextStyle(fontSize: 12),
          ),
          Text(
            'Updated=${_formatUtc(session.updatedAtUtc)} | '
            'Started=${_formatUtc(session.startedAtUtc)} | '
            'Ended=${_formatUtc(session.endedAtUtc)}',
            style: const TextStyle(fontSize: 12),
          ),
          if (session.interruptedAtUtc != null)
            Text(
              'Interrupted=${_formatUtc(session.interruptedAtUtc)}',
              style: const TextStyle(fontSize: 12),
            ),
          if (session.sessionId.trim() != normalizedSessionDocumentId)
            Text(
              'docId=$docIdLabel',
              style: const TextStyle(fontSize: 12),
            ),
          const SizedBox(height: 6),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              OutlinedButton(
                onPressed: () => _useTargetUid(session.therapistId),
                child: const Text('Use therapist UID'),
              ),
              OutlinedButton(
                onPressed: () {
                  setState(() {
                    _sessionTherapistFilter = session.therapistId;
                  });
                },
                child: const Text('Filter therapist'),
              ),
              OutlinedButton(
                onPressed: () {
                  setState(() {
                    _sessionStudentFilter = session.studentId;
                  });
                },
                child: const Text('Filter student'),
              ),
              FilledButton.tonal(
                onPressed: () {
                  final isExpanded =
                      _selectedSessionDocumentId == normalizedSessionDocumentId;
                  if (isExpanded) {
                    setState(() {
                      _selectedSessionDocumentId = '';
                    });
                    return;
                  }
                  _selectSessionForEvents(session);
                },
                child: Text(
                  _selectedSessionDocumentId == normalizedSessionDocumentId
                      ? 'Hide events'
                      : 'Show events',
                ),
              ),
            ],
          ),
          if (_selectedSessionDocumentId == normalizedSessionDocumentId) ...[
            const SizedBox(height: 8),
            _buildSessionEventsInline(session),
          ],
        ],
      ),
    );
  }

  Widget _buildSessionEventsInline(AdminTherapySessionRow session) {
    final normalizedSessionDocumentId = session.documentId.trim();
    final sessionLabel = _anonymizeSessionData
        ? _anonymizedToken(session.sessionId)
        : session.sessionId;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(10),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: Colors.blueGrey.shade100),
      ),
      child: StreamBuilder<List<AdminSessionEventRow>>(
        stream: EntitlementAdminService.watchSessionEvents(
          sessionId: normalizedSessionDocumentId,
          limit: 120,
        ),
        builder: (context, snapshot) {
          if (snapshot.hasError) {
            return Text(
              'Blad odczytu events: ${snapshot.error}',
              style: TextStyle(color: Colors.red.shade700),
            );
          }

          if (!snapshot.hasData) {
            return const Padding(
              padding: EdgeInsets.all(8),
              child: Center(
                child: CircularProgressIndicator(),
              ),
            );
          }

          final events = snapshot.data!;
          if (events.isEmpty) {
            return Text(
              'Brak eventow dla docId=$normalizedSessionDocumentId.',
            );
          }

          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Events for $sessionLabel (${events.length})',
                style: const TextStyle(fontWeight: FontWeight.w700),
              ),
              const SizedBox(height: 8),
              for (final event in events)
                Container(
                  margin: const EdgeInsets.only(bottom: 8),
                  padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(
                    color: Colors.grey.shade100,
                    borderRadius: BorderRadius.circular(8),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        '${event.eventType.isEmpty ? 'UNKNOWN_EVENT' : event.eventType} @ ${_formatUtc(event.eventAtUtc)}',
                        style: const TextStyle(fontWeight: FontWeight.w600),
                      ),
                      const SizedBox(height: 2),
                      Text(
                        'Source=${event.source.isEmpty ? '-' : event.source} | '
                        'Game=${event.gameId.isEmpty ? '-' : event.gameId}',
                        style: const TextStyle(fontSize: 12),
                      ),
                      Text(
                        'TimelineId=${event.timelineEventId.isEmpty ? '-' : event.timelineEventId}',
                        style: const TextStyle(fontSize: 12),
                      ),
                      Text(
                        'Details=${event.detailsPreview()}',
                        style: const TextStyle(fontSize: 12),
                      ),
                    ],
                  ),
                ),
            ],
          );
        },
      ),
    );
  }

  Widget _buildGamePackageOpsGuideCard() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: const [
            Text(
              'Game Package Delivery Guide',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            SizedBox(height: 8),
            Text(
              'Where to upload game package files and how to roll out updates:',
            ),
            SizedBox(height: 6),
            Text(
              '1. Put package manifest/content file under hosting/public/content/ (for example board_demo_probe_1_0_0.pkg.json).',
              style: TextStyle(fontSize: 12),
            ),
            Text(
              '1a. Generate board-safe manifests for runtime games: scripts/build_board_safe_game_packages.ps1 (creates demo_cube_clicker_1_2_0.pkg.json and pulse_target_tap_1_0_0.pkg.json).',
              style: TextStyle(fontSize: 12),
            ),
            Text(
              '2. Deploy hosting from repo root: scripts/deploy_admin_console_hosting.ps1 -ProjectId theraply-vr-demo -CleanBuild',
              style: TextStyle(fontSize: 12),
            ),
            Text(
              '2a. Alternative (external hosting, e.g. Home.pl): scripts/upload_board_safe_game_packages.ps1 -Protocol ftps -FtpHost <ftp-host> -Username <user> -RemoteDirectory /public_html/content',
              style: TextStyle(fontSize: 12),
            ),
            Text(
              '3. In game_catalog set/update packageUri (for example https://theraply-vr-demo.web.app/content/your_file.pkg.json).',
              style: TextStyle(fontSize: 12),
            ),
            Text(
              '4. For update: upload new file, change targetContentVersion + packageUri, then save game_catalog entry.',
              style: TextStyle(fontSize: 12),
            ),
            SizedBox(height: 6),
            Text(
              'Current CMS edits metadata only; binary package upload is done via repo + Firebase Hosting deploy.',
              style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildGameCatalogCrudCard() {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'game_catalog CRUD',
              style: TextStyle(fontWeight: FontWeight.w700),
            ),
            const SizedBox(height: 8),
            const Text(
              'Live Firestore metadata editor for create/update/deactivate/delete. Create dialog starts in quick mode, advanced fields are optional.',
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                FilledButton.icon(
                  onPressed:
                      _savingCatalogEntry ? null : _createGameCatalogEntry,
                  icon: _savingCatalogEntry
                      ? const SizedBox(
                          width: 14,
                          height: 14,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Icon(Icons.add),
                  label: const Text('Create entry (quick)'),
                ),
                OutlinedButton.icon(
                  onPressed: _loadCatalogAuthoringAssets,
                  icon: const Icon(Icons.sync),
                  label: const Text('Refresh seed status'),
                ),
              ],
            ),
            const SizedBox(height: 8),
            StreamBuilder<Map<String, AdminGameGrantStats>>(
              stream: EntitlementAdminService.watchGameGrantStats(),
              builder: (context, statsSnapshot) {
                final statsByGameId =
                    statsSnapshot.data ?? const <String, AdminGameGrantStats>{};
                return StreamBuilder<List<AdminGameCatalogSeedEntry>>(
                  stream: EntitlementAdminService.watchGameCatalog(limit: 500),
                  builder: (context, snapshot) {
                    if (snapshot.hasError) {
                      return Text(
                        'Blad odczytu game_catalog: ${snapshot.error}',
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

                    final rows = snapshot.data!;
                    if (rows.isEmpty) {
                      return const Text('Brak rekordow game_catalog.');
                    }

                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text('Live rows: ${rows.length}'),
                        const SizedBox(height: 8),
                        for (final row in rows)
                          Container(
                            margin: const EdgeInsets.only(bottom: 8),
                            padding: const EdgeInsets.all(8),
                            decoration: BoxDecoration(
                              color: Colors.grey.shade100,
                              borderRadius: BorderRadius.circular(8),
                            ),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Row(
                                  children: [
                                    Expanded(
                                      child: Text(
                                        row.title.trim().isEmpty
                                            ? row.gameId
                                            : row.title,
                                        style: const TextStyle(
                                          fontWeight: FontWeight.w700,
                                        ),
                                      ),
                                    ),
                                    Container(
                                      padding: const EdgeInsets.symmetric(
                                        horizontal: 8,
                                        vertical: 3,
                                      ),
                                      decoration: BoxDecoration(
                                        color: row.active
                                            ? Colors.green.shade100
                                            : Colors.red.shade100,
                                        borderRadius:
                                            BorderRadius.circular(999),
                                      ),
                                      child: Text(
                                        row.active ? 'ACTIVE' : 'INACTIVE',
                                        style: TextStyle(
                                          fontSize: 11,
                                          fontWeight: FontWeight.w700,
                                          color: row.active
                                              ? Colors.green.shade800
                                              : Colors.red.shade800,
                                        ),
                                      ),
                                    ),
                                  ],
                                ),
                                const SizedBox(height: 4),
                                Text(
                                  'gameId=${row.gameId} | sortOrder=${row.sortOrder} | delivery=${row.deliveryMode}',
                                  style: const TextStyle(fontSize: 12),
                                ),
                                Text(
                                  'targetVersion=${row.targetContentVersion} | launchEnabled=${row.runtimeLaunchEnabled}',
                                  style: const TextStyle(fontSize: 12),
                                ),
                                Text(
                                  'purchasable=${row.availableForPurchase} | '
                                  'explicitLicense=${row.requiresExplicitLicense} | '
                                  'saveResume=${row.supportsSaveResume}',
                                  style: const TextStyle(fontSize: 12),
                                ),
                                if (row.packageUri.trim().isNotEmpty)
                                  Text(
                                    'packageUri=${row.packageUri}',
                                    style: const TextStyle(fontSize: 12),
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                  ),
                                if (row.description.trim().isNotEmpty)
                                  Text(
                                    row.description,
                                    style: const TextStyle(fontSize: 12),
                                  ),
                                Text(
                                  'Grant stats: active=${statsByGameId[row.gameId]?.activeAssignments ?? 0}, '
                                  'revoked=${statsByGameId[row.gameId]?.revokedAssignments ?? 0}',
                                  style: const TextStyle(fontSize: 12),
                                ),
                                const SizedBox(height: 6),
                                Wrap(
                                  spacing: 8,
                                  runSpacing: 8,
                                  children: [
                                    TextButton(
                                      onPressed: () =>
                                          _useGameIdForGrant(row.gameId),
                                      child: const Text('Use gameId'),
                                    ),
                                    OutlinedButton(
                                      onPressed: _savingCatalogEntry
                                          ? null
                                          : () => _editGameCatalogEntry(row),
                                      child: const Text('Edit'),
                                    ),
                                    OutlinedButton(
                                      onPressed: _savingCatalogEntry ||
                                              !row.active
                                          ? null
                                          : () => _deactivateGameCatalogEntry(
                                              row.gameId),
                                      child: const Text('Deactivate'),
                                    ),
                                    OutlinedButton(
                                      onPressed: _savingCatalogEntry
                                          ? null
                                          : () => _deleteGameCatalogEntry(
                                              row.gameId),
                                      child: Text(
                                        'Delete',
                                        style: TextStyle(
                                          color: Colors.red.shade700,
                                        ),
                                      ),
                                    ),
                                  ],
                                ),
                              ],
                            ),
                          ),
                      ],
                    );
                  },
                );
              },
            ),
          ],
        ),
      ),
    );
  }

  bool _matchesGamesAuthoringFilter(_AuthoringStatus status) {
    switch (_gamesAuthoringFilter) {
      case _GamesAuthoringFilter.all:
        return true;
      case _GamesAuthoringFilter.issues:
        return status.level == _AuthoringStatusLevel.error ||
            status.level == _AuthoringStatusLevel.warning;
      case _GamesAuthoringFilter.ready:
        return status.level == _AuthoringStatusLevel.healthy;
      case _GamesAuthoringFilter.info:
        return status.level == _AuthoringStatusLevel.info;
    }
  }

  int _authoringSeverityRank(_AuthoringStatusLevel level) {
    switch (level) {
      case _AuthoringStatusLevel.error:
        return 0;
      case _AuthoringStatusLevel.warning:
        return 1;
      case _AuthoringStatusLevel.info:
        return 2;
      case _AuthoringStatusLevel.healthy:
        return 3;
    }
  }

  String _authoringFilterLabel(_GamesAuthoringFilter filter) {
    switch (filter) {
      case _GamesAuthoringFilter.all:
        return 'All';
      case _GamesAuthoringFilter.issues:
        return 'Issues';
      case _GamesAuthoringFilter.ready:
        return 'Ready';
      case _GamesAuthoringFilter.info:
        return 'Info';
    }
  }

  String _gamesSortModeLabel(_GamesSortMode mode) {
    switch (mode) {
      case _GamesSortMode.authoringSeverity:
        return 'Sort: issues first';
      case _GamesSortMode.catalogOrder:
        return 'Sort: catalog order';
    }
  }

  Widget _buildGamesTab() {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: [
        _buildGamePackageOpsGuideCard(),
        const SizedBox(height: 12),
        _buildGameCatalogCrudCard(),
        const SizedBox(height: 12),
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
                Text(
                  (_loadingCatalogSeed || _loadingExportManifest)
                      ? 'Loading catalog seed and export manifest from contracts...'
                      : 'Catalog seed and export manifest are loaded from assets/contracts. You can seed game_catalog with one click.',
                ),
                const SizedBox(height: 8),
                _buildSeedFreshnessCard(),
                const SizedBox(height: 10),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    ElevatedButton.icon(
                      onPressed: (_seedingGameCatalog || _loadingCatalogSeed)
                          ? null
                          : _seedGameCatalog,
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
                      onPressed: (_loadingCatalogSeed || _loadingExportManifest)
                          ? null
                          : _loadCatalogAuthoringAssets,
                      icon: (_loadingCatalogSeed || _loadingExportManifest)
                          ? const SizedBox(
                              width: 14,
                              height: 14,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.refresh),
                      label: const Text('Reload seed'),
                    ),
                    OutlinedButton.icon(
                      onPressed: _useCurrentUserUidAsTarget,
                      icon: const Icon(Icons.person_pin),
                      label: const Text('Use my UID'),
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final filter in _GamesAuthoringFilter.values)
                      ChoiceChip(
                        selected: _gamesAuthoringFilter == filter,
                        label: Text(_authoringFilterLabel(filter)),
                        onSelected: (_) {
                          setState(() {
                            _gamesAuthoringFilter = filter;
                          });
                        },
                      ),
                  ],
                ),
                const SizedBox(height: 8),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final mode in _GamesSortMode.values)
                      ChoiceChip(
                        selected: _gamesSortMode == mode,
                        label: Text(_gamesSortModeLabel(mode)),
                        onSelected: (_) {
                          setState(() {
                            _gamesSortMode = mode;
                          });
                        },
                      ),
                  ],
                ),
                const SizedBox(height: 10),
                StreamBuilder<Map<String, AdminGameGrantStats>>(
                  stream: EntitlementAdminService.watchGameGrantStats(),
                  builder: (context, snapshot) {
                    final statsByGameId =
                        snapshot.data ?? const <String, AdminGameGrantStats>{};
                    final manifestEntriesByGameId =
                        <String, GameDefinitionExportManifestEntry>{
                      for (final entry in _exportManifest?.entries ??
                          const <GameDefinitionExportManifestEntry>[])
                        entry.gameId.toLowerCase(): entry,
                    };
                    final knownIds = _catalogSeedEntries
                        .map((entry) => entry.gameId)
                        .toSet();
                    final unknownGrantGames = statsByGameId.keys
                        .where((gameId) => !knownIds.contains(gameId))
                        .toList()
                      ..sort();
                    final catalogRows = _catalogSeedEntries.map((game) {
                      final manifestEntry =
                          manifestEntriesByGameId[game.gameId.toLowerCase()];
                      final authoringStatus = _resolveAuthoringStatus(
                        isCatalogEntry: true,
                        runtimeLaunchEnabled: game.runtimeLaunchEnabled,
                        manifestEntry: manifestEntry,
                        hasEmbeddedMobileSchema:
                            game.mobileControlSchema != null &&
                                game.mobileControlSchema!.isNotEmpty,
                      );
                      return _CatalogGameCardData(
                        entry: game,
                        authoringStatus: authoringStatus,
                      );
                    }).toList();
                    catalogRows.sort((left, right) {
                      if (_gamesSortMode == _GamesSortMode.authoringSeverity) {
                        final byAuthoring = _authoringSeverityRank(
                          left.authoringStatus.level,
                        ).compareTo(
                          _authoringSeverityRank(right.authoringStatus.level),
                        );
                        if (byAuthoring != 0) {
                          return byAuthoring;
                        }
                      }

                      final bySortOrder =
                          left.entry.sortOrder.compareTo(right.entry.sortOrder);
                      if (bySortOrder != 0) {
                        return bySortOrder;
                      }
                      return left.entry.gameId
                          .toLowerCase()
                          .compareTo(right.entry.gameId.toLowerCase());
                    });
                    final visibleCatalogRows = catalogRows
                        .where((row) =>
                            _matchesGamesAuthoringFilter(row.authoringStatus))
                        .toList();

                    final unknownGrantRows = unknownGrantGames.map((gameId) {
                      final manifestEntry =
                          manifestEntriesByGameId[gameId.toLowerCase()];
                      return _GrantOnlyGameCardData(
                        gameId: gameId,
                        authoringStatus: _resolveAuthoringStatus(
                          isCatalogEntry: false,
                          runtimeLaunchEnabled: false,
                          manifestEntry: manifestEntry,
                          hasEmbeddedMobileSchema: false,
                        ),
                      );
                    }).toList();
                    unknownGrantRows.sort((left, right) {
                      if (_gamesSortMode == _GamesSortMode.authoringSeverity) {
                        final byAuthoring = _authoringSeverityRank(
                          left.authoringStatus.level,
                        ).compareTo(
                          _authoringSeverityRank(right.authoringStatus.level),
                        );
                        if (byAuthoring != 0) {
                          return byAuthoring;
                        }
                      }
                      return left.gameId
                          .toLowerCase()
                          .compareTo(right.gameId.toLowerCase());
                    });
                    final visibleUnknownGrantRows = unknownGrantRows
                        .where((row) =>
                            _matchesGamesAuthoringFilter(row.authoringStatus))
                        .toList();

                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Visible: ${visibleCatalogRows.length + visibleUnknownGrantRows.length}',
                          style: const TextStyle(fontSize: 12),
                        ),
                        Text(
                          'Catalog entries: ${visibleCatalogRows.length}/${catalogRows.length}',
                          style: const TextStyle(fontSize: 12),
                        ),
                        if (unknownGrantRows.isNotEmpty)
                          Text(
                            'Grant-only entries: ${visibleUnknownGrantRows.length}/${unknownGrantRows.length}',
                            style: const TextStyle(fontSize: 12),
                          ),
                        const SizedBox(height: 8),
                        for (final row in visibleCatalogRows)
                          _buildGameCard(
                            title: row.entry.title,
                            gameId: row.entry.gameId,
                            description: row.entry.description,
                            targetVersion: row.entry.targetContentVersion,
                            packageUri: row.entry.packageUri,
                            availableForPurchase:
                                row.entry.availableForPurchase,
                            runtimeLaunchEnabled:
                                row.entry.runtimeLaunchEnabled,
                            sortOrder: row.entry.sortOrder,
                            stats: statsByGameId[row.entry.gameId],
                            authoringStatus: row.authoringStatus,
                          ),
                        if (visibleUnknownGrantRows.isNotEmpty) ...[
                          const SizedBox(height: 8),
                          const Align(
                            alignment: Alignment.centerLeft,
                            child: Text(
                              'Granty dla innych gameId:',
                              style: TextStyle(fontWeight: FontWeight.w600),
                            ),
                          ),
                          const SizedBox(height: 8),
                          for (final row in visibleUnknownGrantRows)
                            _buildGameCard(
                              title: row.gameId,
                              gameId: row.gameId,
                              description: 'GAME scope detected in grants.',
                              targetVersion: '-',
                              packageUri: '',
                              availableForPurchase: false,
                              runtimeLaunchEnabled: false,
                              sortOrder: 0,
                              stats: statsByGameId[row.gameId],
                              authoringStatus: row.authoringStatus,
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
    required _AuthoringStatus authoringStatus,
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
                const SizedBox(height: 4),
                _buildAuthoringStatusRow(authoringStatus),
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

  _AuthoringStatus _resolveAuthoringStatus({
    required bool isCatalogEntry,
    required bool runtimeLaunchEnabled,
    required GameDefinitionExportManifestEntry? manifestEntry,
    required bool hasEmbeddedMobileSchema,
  }) {
    if (!isCatalogEntry) {
      return const _AuthoringStatus(
        level: _AuthoringStatusLevel.info,
        label: 'grant-only',
        summary: 'Grant exists for gameId outside catalog seed.',
        details: <String>[],
      );
    }

    if (_loadingExportManifest) {
      return const _AuthoringStatus(
        level: _AuthoringStatusLevel.info,
        label: 'checking',
        summary: 'Waiting for export manifest.',
        details: <String>[],
      );
    }

    if (_exportManifestError.trim().isNotEmpty) {
      return _AuthoringStatus(
        level: _AuthoringStatusLevel.error,
        label: 'error',
        summary: 'Export manifest load failed.',
        details: <String>['error=$_exportManifestError'],
      );
    }

    if (manifestEntry == null) {
      if (runtimeLaunchEnabled) {
        return const _AuthoringStatus(
          level: _AuthoringStatusLevel.warning,
          label: 'missing export',
          summary:
              'Runtime launch is enabled but game is absent from export manifest.',
          details: <String>[],
        );
      }

      return const _AuthoringStatus(
        level: _AuthoringStatusLevel.info,
        label: 'store-only',
        summary: 'Catalog entry is not part of runtime authoring export.',
        details: <String>[],
      );
    }

    final schemaPath = manifestEntry.mobileControlSchemaJsonPath.trim();
    final manifestHasSchemaPath = schemaPath.isNotEmpty;

    if (manifestHasSchemaPath && hasEmbeddedMobileSchema) {
      return _AuthoringStatus(
        level: _AuthoringStatusLevel.healthy,
        label: 'export+schema',
        summary: 'Definition export and embedded mobile schema are present.',
        details: <String>[
          'definition=${manifestEntry.definitionJsonPath}',
          'schema=$schemaPath',
        ],
      );
    }

    if (!manifestHasSchemaPath && !hasEmbeddedMobileSchema) {
      return _AuthoringStatus(
        level: _AuthoringStatusLevel.warning,
        label: 'export-only',
        summary: 'Definition export exists without mobile control schema.',
        details: <String>['definition=${manifestEntry.definitionJsonPath}'],
      );
    }

    if (manifestHasSchemaPath && !hasEmbeddedMobileSchema) {
      return _AuthoringStatus(
        level: _AuthoringStatusLevel.error,
        label: 'schema mismatch',
        summary:
            'Manifest has schema path but catalog entry has no embedded mobile schema.',
        details: <String>['schema=$schemaPath'],
      );
    }

    return _AuthoringStatus(
      level: _AuthoringStatusLevel.error,
      label: 'schema mismatch',
      summary:
          'Catalog entry embeds mobile schema but manifest lacks schema path.',
      details: <String>['definition=${manifestEntry.definitionJsonPath}'],
    );
  }

  Widget _buildAuthoringStatusRow(_AuthoringStatus status) {
    final Color badgeBackground;
    final Color badgeForeground;
    switch (status.level) {
      case _AuthoringStatusLevel.healthy:
        badgeBackground = Colors.green.shade100;
        badgeForeground = Colors.green.shade900;
        break;
      case _AuthoringStatusLevel.warning:
        badgeBackground = Colors.orange.shade100;
        badgeForeground = Colors.orange.shade900;
        break;
      case _AuthoringStatusLevel.error:
        badgeBackground = Colors.red.shade100;
        badgeForeground = Colors.red.shade900;
        break;
      case _AuthoringStatusLevel.info:
        badgeBackground = Colors.blueGrey.shade100;
        badgeForeground = Colors.blueGrey.shade900;
        break;
    }

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(8),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(6),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                decoration: BoxDecoration(
                  color: badgeBackground,
                  borderRadius: BorderRadius.circular(999),
                ),
                child: Text(
                  status.label,
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w600,
                    color: badgeForeground,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              const Text(
                'authoring',
                style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
              ),
            ],
          ),
          const SizedBox(height: 4),
          Text(status.summary, style: const TextStyle(fontSize: 12)),
          if (status.details.isNotEmpty) ...[
            const SizedBox(height: 2),
            for (final detail in status.details)
              Text(detail, style: const TextStyle(fontSize: 11)),
          ],
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return DefaultTabController(
      length: 5,
      child: Scaffold(
        appBar: AppBar(
          surfaceTintColor: Colors.transparent,
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
              Tab(text: 'Sessions'),
              Tab(text: 'Games'),
            ],
          ),
        ),
        body: LayoutBuilder(
          builder: (context, constraints) {
            return Align(
              alignment: Alignment.topCenter,
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 1560),
                child: TabBarView(
                  children: [
                    _buildOperationsTab(),
                    _buildAccountsTab(),
                    _buildChildrenTab(),
                    _buildSessionsTab(),
                    _buildGamesTab(),
                  ],
                ),
              ),
            );
          },
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

enum _SeedFreshnessLevel {
  loading,
  healthy,
  warning,
  stale,
  error,
}

class _SeedFreshnessStatus {
  final _SeedFreshnessLevel level;
  final String label;
  final String summary;
  final List<String> details;

  const _SeedFreshnessStatus({
    required this.level,
    required this.label,
    required this.summary,
    required this.details,
  });
}

enum _AuthoringStatusLevel {
  info,
  healthy,
  warning,
  error,
}

class _AuthoringStatus {
  final _AuthoringStatusLevel level;
  final String label;
  final String summary;
  final List<String> details;

  const _AuthoringStatus({
    required this.level,
    required this.label,
    required this.summary,
    required this.details,
  });
}

enum _GamesAuthoringFilter {
  all,
  issues,
  ready,
  info,
}

enum _GamesSortMode {
  authoringSeverity,
  catalogOrder,
}

enum _SessionStateFilter {
  all,
  active,
  terminal,
  interrupted,
}

class _CatalogGameCardData {
  final AdminGameCatalogSeedEntry entry;
  final _AuthoringStatus authoringStatus;

  const _CatalogGameCardData({
    required this.entry,
    required this.authoringStatus,
  });
}

class _GrantOnlyGameCardData {
  final String gameId;
  final _AuthoringStatus authoringStatus;

  const _GrantOnlyGameCardData({
    required this.gameId,
    required this.authoringStatus,
  });
}

class _AccountCreateRequest {
  final String email;
  final String password;
  final EntitlementRole role;
  final SubscriptionPlanTier planTier;
  final LicenseStatus appStatus;
  final bool perpetual;
  final int expiresInDays;

  const _AccountCreateRequest({
    required this.email,
    required this.password,
    required this.role,
    required this.planTier,
    required this.appStatus,
    required this.perpetual,
    required this.expiresInDays,
  });
}

class _StudentEditorResult {
  final String studentId;
  final String therapistId;
  final String firstName;
  final String lastName;

  const _StudentEditorResult({
    required this.studentId,
    required this.therapistId,
    required this.firstName,
    required this.lastName,
  });
}
