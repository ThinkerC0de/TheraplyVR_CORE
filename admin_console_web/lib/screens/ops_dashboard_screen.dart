import 'dart:math';

import 'package:flutter/material.dart';
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
  final TextEditingController _targetUserIdController = TextEditingController();
  final TextEditingController _entitlementExpiresDaysController =
      TextEditingController(text: '365');
  final TextEditingController _grantGameIdController = TextEditingController();
  final TextEditingController _grantExpiresDaysController =
      TextEditingController(text: '30');
  final TextEditingController _grantNoteController = TextEditingController();

  EntitlementRole _selectedRole = EntitlementRole.therapist;
  LicenseStatus _selectedAppLicenseStatus = LicenseStatus.active;
  bool _entitlementPerpetual = true;

  EntitlementGrantScope _selectedGrantScope = EntitlementGrantScope.app;
  LicenseStatus _selectedGrantStatus = LicenseStatus.active;
  bool _grantPerpetual = true;
  bool _grantRevoked = false;
  EntitlementRole? _grantRoleOverride;

  bool _savingEntitlement = false;
  bool _savingGrant = false;

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
    _entitlementExpiresDaysController.dispose();
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

  Future<void> _saveEntitlement() async {
    if (_targetUserId.isEmpty) {
      _snack('Podaj UID uzytkownika', error: true);
      return;
    }

    setState(() {
      _savingEntitlement = true;
    });

    try {
      await EntitlementAdminService.upsertUserEntitlement(
        userId: _targetUserId,
        role: _selectedRole,
        appLicense: _buildGrant(
          status: _selectedAppLicenseStatus,
          perpetual: _entitlementPerpetual,
          days: _readDays(_entitlementExpiresDaysController, 365),
        ),
      );
      _snack('Zapisano user_entitlements/$_targetUserId');
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

    setState(() {
      _savingGrant = true;
    });

    final nowUtc = DateTime.now().toUtc();
    try {
      final assignment = EntitlementGrantAssignment(
        grantId: '',
        granteeUserId: _targetUserId,
        scope: _selectedGrantScope,
        gameId: _selectedGrantScope == EntitlementGrantScope.game ? gameId : null,
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
      );

      final grantId = await EntitlementAdminService.upsertGrantAssignment(
        assignment: assignment,
      );
      _snack('Utworzono grant $grantId');
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
        ),
      );
      _snack('Grant ${grant.grantId} revoked');
    } catch (e) {
      _snack('Blad revoke: $e', error: true);
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

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Theraply Entitlement Admin'),
        actions: [
          if (FirebaseService.currentUser != null)
            Padding(
              padding: const EdgeInsets.only(right: 12),
              child: Center(
                child: Text(
                  FirebaseService.currentUser!.email ?? FirebaseService.currentUser!.uid,
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
          _buildLiveCard(),
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
                if (value == null) return;
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
                DropdownMenuItem(value: EntitlementGrantScope.app, child: Text('APP')),
                DropdownMenuItem(value: EntitlementGrantScope.game, child: Text('GAME')),
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
                DropdownMenuItem(value: LicenseStatus.active, child: Text('ACTIVE')),
                DropdownMenuItem(value: LicenseStatus.expired, child: Text('EXPIRED')),
                DropdownMenuItem(value: LicenseStatus.revoked, child: Text('REVOKED')),
                DropdownMenuItem(value: LicenseStatus.none, child: Text('NONE')),
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
              stream: EntitlementAdminService.watchEntitlementProfile(_targetUserId),
              builder: (context, snapshot) {
                final profile = snapshot.data;
                if (profile == null) {
                  return const Text('Entitlement profile: brak');
                }

                return Text(
                  'Role=${profile.role.wireValue}, '
                  'App=${profile.appLicense.status.wireValue}, '
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
