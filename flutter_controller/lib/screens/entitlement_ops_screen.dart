import 'dart:math';

import 'package:firebase_auth/firebase_auth.dart';
import 'package:flutter/material.dart';
import 'package:flutter_controller/models/entitlement_access.dart';
import 'package:flutter_controller/models/entitlement_grant_contract.dart';
import 'package:flutter_controller/services/entitlement_service.dart';

class EntitlementOpsScreen extends StatefulWidget {
  const EntitlementOpsScreen({super.key});

  @override
  State<EntitlementOpsScreen> createState() => _EntitlementOpsScreenState();
}

class _EntitlementOpsScreenState extends State<EntitlementOpsScreen> {
  final TextEditingController _targetUserIdController = TextEditingController();
  final TextEditingController _grantGameIdController = TextEditingController();
  final TextEditingController _grantNoteController = TextEditingController();
  final TextEditingController _appExpiresDaysController =
      TextEditingController(text: '365');
  final TextEditingController _grantExpiresDaysController =
      TextEditingController(text: '30');

  EntitlementRole _selectedRole = EntitlementRole.therapist;
  LicenseStatus _selectedAppLicenseStatus = LicenseStatus.active;
  bool _appPerpetual = true;

  EntitlementGrantScope _selectedGrantScope = EntitlementGrantScope.app;
  LicenseStatus _selectedGrantStatus = LicenseStatus.active;
  bool _grantPerpetual = true;
  bool _grantRevoked = false;
  EntitlementRole? _grantRoleOverride;

  bool _isSavingEntitlement = false;
  bool _isSavingGrant = false;

  @override
  void initState() {
    super.initState();
    _targetUserIdController.addListener(_handleTargetUserChanged);
  }

  @override
  void dispose() {
    _targetUserIdController.removeListener(_handleTargetUserChanged);
    _targetUserIdController.dispose();
    _grantGameIdController.dispose();
    _grantNoteController.dispose();
    _appExpiresDaysController.dispose();
    _grantExpiresDaysController.dispose();
    super.dispose();
  }

  String get _targetUserId => _targetUserIdController.text.trim();

  void _handleTargetUserChanged() {
    setState(() {});
  }

  int _safeDaysFromController(TextEditingController controller, int fallback) {
    final parsed = int.tryParse(controller.text.trim());
    if (parsed == null) {
      return fallback;
    }
    return max(1, parsed);
  }

  LicenseGrant _buildLicenseGrant({
    required LicenseStatus status,
    required bool perpetual,
    required int expiresInDays,
  }) {
    final nowUtc = DateTime.now().toUtc();
    return LicenseGrant(
      status: status,
      fromUtc: nowUtc,
      toUtc: perpetual ? null : nowUtc.add(Duration(days: expiresInDays)),
      perpetual: perpetual,
    );
  }

  Future<void> _saveUserEntitlement() async {
    final userId = _targetUserId;
    if (userId.isEmpty) {
      _showSnack('Podaj UID użytkownika', isError: true);
      return;
    }

    setState(() {
      _isSavingEntitlement = true;
    });

    try {
      final appGrant = _buildLicenseGrant(
        status: _selectedAppLicenseStatus,
        perpetual: _appPerpetual,
        expiresInDays: _safeDaysFromController(_appExpiresDaysController, 365),
      );

      await EntitlementService.upsertUserEntitlement(
        userId: userId,
        role: _selectedRole,
        appLicense: appGrant,
      );

      _showSnack('Zapisano user_entitlements/$userId');
    } catch (e) {
      _showSnack('Błąd zapisu entitlement: $e', isError: true);
    } finally {
      if (mounted) {
        setState(() {
          _isSavingEntitlement = false;
        });
      }
    }
  }

  Future<void> _saveGrant() async {
    final userId = _targetUserId;
    if (userId.isEmpty) {
      _showSnack('Podaj UID użytkownika', isError: true);
      return;
    }

    if (_selectedGrantScope == EntitlementGrantScope.game &&
        _grantGameIdController.text.trim().isEmpty) {
      _showSnack('Dla scope=GAME podaj gameId', isError: true);
      return;
    }

    setState(() {
      _isSavingGrant = true;
    });

    final nowUtc = DateTime.now().toUtc();
    try {
      final grant = EntitlementGrantAssignment(
        grantId: '',
        granteeUserId: userId,
        scope: _selectedGrantScope,
        gameId: _selectedGrantScope == EntitlementGrantScope.game
            ? _grantGameIdController.text.trim()
            : null,
        licenseGrant: _buildLicenseGrant(
          status: _selectedGrantStatus,
          perpetual: _grantPerpetual,
          expiresInDays:
              _safeDaysFromController(_grantExpiresDaysController, 30),
        ),
        source: EntitlementGrantSource.admin,
        assignedBy: FirebaseAuth.instance.currentUser?.uid ?? 'ops-panel',
        assignedAtUtc: nowUtc,
        revoked: _grantRevoked,
        revokedAtUtc: _grantRevoked ? nowUtc : null,
        roleOverride: _grantRoleOverride,
        note: _grantNoteController.text.trim().isEmpty
            ? null
            : _grantNoteController.text.trim(),
      );

      final grantId = await EntitlementService.upsertGrantAssignment(
        assignment: grant,
      );
      _showSnack('Zapisano grant: $grantId');
    } catch (e) {
      _showSnack('Błąd zapisu grantu: $e', isError: true);
    } finally {
      if (mounted) {
        setState(() {
          _isSavingGrant = false;
        });
      }
    }
  }

  Future<void> _revokeGrant(EntitlementGrantAssignment assignment) async {
    try {
      final nowUtc = DateTime.now().toUtc();
      await EntitlementService.upsertGrantAssignment(
        assignment: EntitlementGrantAssignment(
          grantId: assignment.grantId,
          granteeUserId: assignment.granteeUserId,
          scope: assignment.scope,
          gameId: assignment.gameId,
          licenseGrant: assignment.licenseGrant,
          source: assignment.source,
          assignedBy: assignment.assignedBy,
          assignedAtUtc: assignment.assignedAtUtc,
          revoked: true,
          revokedAtUtc: nowUtc,
          roleOverride: assignment.roleOverride,
          note: assignment.note,
        ),
      );
      _showSnack('Grant ${assignment.grantId} oznaczony jako revoked');
    } catch (e) {
      _showSnack('Błąd revoke: $e', isError: true);
    }
  }

  void _showSnack(String message, {bool isError = false}) {
    if (!mounted) {
      return;
    }
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
        backgroundColor: isError ? Colors.red.shade700 : null,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Entitlement Ops'),
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          TextField(
            controller: _targetUserIdController,
            decoration: const InputDecoration(
              labelText: 'Target User UID',
              border: OutlineInputBorder(),
            ),
          ),
          const SizedBox(height: 12),
          _buildEntitlementCard(),
          const SizedBox(height: 12),
          _buildGrantCard(),
          const SizedBox(height: 12),
          _buildLiveSnapshotCard(),
        ],
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
                if (value == null) {
                  return;
                }
                setState(() {
                  _selectedRole = value;
                });
              },
            ),
            const SizedBox(height: 8),
            DropdownButtonFormField<LicenseStatus>(
              initialValue: _selectedAppLicenseStatus,
              decoration: const InputDecoration(
                labelText: 'App License Status',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(value: LicenseStatus.active, child: Text('ACTIVE')),
                DropdownMenuItem(value: LicenseStatus.expired, child: Text('EXPIRED')),
                DropdownMenuItem(value: LicenseStatus.revoked, child: Text('REVOKED')),
                DropdownMenuItem(value: LicenseStatus.none, child: Text('NONE')),
              ],
              onChanged: (value) {
                if (value == null) {
                  return;
                }
                setState(() {
                  _selectedAppLicenseStatus = value;
                });
              },
            ),
            const SizedBox(height: 8),
            SwitchListTile(
              value: _appPerpetual,
              onChanged: (value) {
                setState(() {
                  _appPerpetual = value;
                });
              },
              title: const Text('Perpetual'),
              contentPadding: EdgeInsets.zero,
            ),
            if (!_appPerpetual)
              TextField(
                controller: _appExpiresDaysController,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Expires In Days',
                  border: OutlineInputBorder(),
                ),
              ),
            const SizedBox(height: 8),
            ElevatedButton.icon(
              onPressed: _isSavingEntitlement ? null : _saveUserEntitlement,
              icon: _isSavingEntitlement
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
                DropdownMenuItem(value: EntitlementGrantScope.app, child: Text('APP')),
                DropdownMenuItem(value: EntitlementGrantScope.game, child: Text('GAME')),
              ],
              onChanged: (value) {
                if (value == null) {
                  return;
                }
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
                labelText: 'Grant License Status',
                border: OutlineInputBorder(),
              ),
              items: const [
                DropdownMenuItem(value: LicenseStatus.active, child: Text('ACTIVE')),
                DropdownMenuItem(value: LicenseStatus.expired, child: Text('EXPIRED')),
                DropdownMenuItem(value: LicenseStatus.revoked, child: Text('REVOKED')),
                DropdownMenuItem(value: LicenseStatus.none, child: Text('NONE')),
              ],
              onChanged: (value) {
                if (value == null) {
                  return;
                }
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
            const SizedBox(height: 8),
            SwitchListTile(
              value: _grantPerpetual,
              onChanged: (value) {
                setState(() {
                  _grantPerpetual = value;
                });
              },
              title: const Text('Perpetual'),
              contentPadding: EdgeInsets.zero,
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
            const SizedBox(height: 8),
            SwitchListTile(
              value: _grantRevoked,
              onChanged: (value) {
                setState(() {
                  _grantRevoked = value;
                });
              },
              title: const Text('Revoked'),
              contentPadding: EdgeInsets.zero,
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
            ElevatedButton.icon(
              onPressed: _isSavingGrant ? null : _saveGrant,
              icon: _isSavingGrant
                  ? const SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Icon(Icons.add_circle_outline),
              label: const Text('Create Grant'),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildLiveSnapshotCard() {
    if (_targetUserId.isEmpty) {
      return const Card(
        child: Padding(
          padding: EdgeInsets.all(12),
          child: Text('Wpisz UID, aby zobaczyc aktualny stan entitlementow.'),
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
              stream: EntitlementService.watchEntitlementProfile(_targetUserId),
              builder: (context, snapshot) {
                final profile = snapshot.data;
                if (profile == null) {
                  return const Text('Entitlement profile: brak dokumentu');
                }

                return Text(
                  'Role=${profile.role.wireValue}, App=${profile.appLicense.status.wireValue}, Policy=${profile.policyVersion ?? '-'}',
                );
              },
            ),
            const SizedBox(height: 8),
            StreamBuilder<List<EntitlementGrantAssignment>>(
              stream: EntitlementService.watchGrantAssignmentsForUser(
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
                          borderRadius: BorderRadius.circular(8),
                          color: Colors.grey.shade100,
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
