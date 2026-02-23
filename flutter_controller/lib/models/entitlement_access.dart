import 'package:cloud_firestore/cloud_firestore.dart';

enum EntitlementRole {
  therapist,
  parent,
  unknown,
}

extension EntitlementRoleCodec on EntitlementRole {
  static EntitlementRole fromWire(String? wireValue) {
    switch (wireValue?.trim().toUpperCase()) {
      case 'THERAPIST':
        return EntitlementRole.therapist;
      case 'PARENT':
        return EntitlementRole.parent;
      default:
        return EntitlementRole.unknown;
    }
  }

  String get wireValue {
    switch (this) {
      case EntitlementRole.therapist:
        return 'THERAPIST';
      case EntitlementRole.parent:
        return 'PARENT';
      case EntitlementRole.unknown:
        return 'UNKNOWN';
    }
  }
}

enum SubscriptionPlanTier {
  free,
  basic,
  premium,
  live,
  unknown,
}

extension SubscriptionPlanTierCodec on SubscriptionPlanTier {
  static SubscriptionPlanTier fromWire(String? wireValue) {
    switch (wireValue?.trim().toUpperCase()) {
      case 'FREE':
        return SubscriptionPlanTier.free;
      case 'BASIC':
        return SubscriptionPlanTier.basic;
      case 'PREMIUM':
        return SubscriptionPlanTier.premium;
      case 'LIVE':
      case 'PLATINUM':
        return SubscriptionPlanTier.live;
      default:
        return SubscriptionPlanTier.unknown;
    }
  }

  String get wireValue {
    switch (this) {
      case SubscriptionPlanTier.free:
        return 'FREE';
      case SubscriptionPlanTier.basic:
        return 'BASIC';
      case SubscriptionPlanTier.premium:
        return 'PREMIUM';
      case SubscriptionPlanTier.live:
        return 'LIVE';
      case SubscriptionPlanTier.unknown:
        return 'UNKNOWN';
    }
  }
}

class EntitlementFeatureKeys {
  static const String vrSessionControl = 'vr_session_control';
  static const String parentGuidedStart = 'parent_guided_start';
  static const String progressInsights = 'progress_insights';
  static const String therapeuticStories = 'therapeutic_stories';
  static const String coloringPrintables = 'coloring_printables';
  static const String premiumModules = 'premium_modules';
  static const String liveTherapistSupport = 'live_therapist_support';
  static const String rewardsUnlocks = 'rewards_unlocks';
}

class EntitlementPlanProfile {
  final SubscriptionPlanTier tier;
  final Map<String, bool> featureFlags;
  final Set<String> allowedGameIds;
  final int demoSessionLimit;
  final int demoSessionsUsed;

  const EntitlementPlanProfile({
    required this.tier,
    required this.featureFlags,
    required this.allowedGameIds,
    required this.demoSessionLimit,
    required this.demoSessionsUsed,
  });

  factory EntitlementPlanProfile.fromMap(Map<String, dynamic>? data) {
    final tier = SubscriptionPlanTierCodec.fromWire(
      data?['tier'] as String? ?? data?['subscriptionPlan'] as String?,
    );
    final defaults = _defaultFeatureFlagsForTier(tier);
    final mergedFlags = <String, bool>{
      ...defaults,
      ..._parseFeatureFlags(data?['featureFlags']),
    };
    final allowedGames = _parseAllowedGameIds(data?['allowedGameIds']);
    final defaultDemoLimit = tier == SubscriptionPlanTier.free ? 2 : 0;

    return EntitlementPlanProfile(
      tier: tier,
      featureFlags: mergedFlags,
      allowedGameIds: allowedGames,
      demoSessionLimit: _readInt(data?['demoSessionLimit'], defaultDemoLimit),
      demoSessionsUsed: _readInt(data?['demoSessionsUsed'], 0),
    );
  }

  bool isFeatureEnabled(String featureKey) {
    final normalized = featureKey.trim();
    if (normalized.isEmpty) {
      return false;
    }
    return featureFlags[normalized] ?? false;
  }

  bool isGameAllowed(String gameId) {
    final normalized = gameId.trim();
    if (normalized.isEmpty) {
      return false;
    }
    if (allowedGameIds.isEmpty) {
      return true;
    }
    return allowedGameIds.contains(normalized);
  }

  bool get hasDemoSessionsRemaining {
    if (demoSessionLimit <= 0) {
      return true;
    }
    return demoSessionsUsed < demoSessionLimit;
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'tier': tier.wireValue,
      'featureFlags': featureFlags,
      'allowedGameIds': allowedGameIds.toList()..sort(),
      'demoSessionLimit': demoSessionLimit,
      'demoSessionsUsed': demoSessionsUsed,
    };
  }

  static Map<String, bool> _defaultFeatureFlagsForTier(
    SubscriptionPlanTier tier,
  ) {
    final defaults = <String, bool>{
      EntitlementFeatureKeys.vrSessionControl: false,
      EntitlementFeatureKeys.parentGuidedStart: false,
      EntitlementFeatureKeys.progressInsights: false,
      EntitlementFeatureKeys.therapeuticStories: false,
      EntitlementFeatureKeys.coloringPrintables: false,
      EntitlementFeatureKeys.premiumModules: false,
      EntitlementFeatureKeys.liveTherapistSupport: false,
      EntitlementFeatureKeys.rewardsUnlocks: false,
    };

    switch (tier) {
      case SubscriptionPlanTier.free:
        defaults[EntitlementFeatureKeys.vrSessionControl] = true;
        defaults[EntitlementFeatureKeys.parentGuidedStart] = true;
        defaults[EntitlementFeatureKeys.progressInsights] = true;
        break;
      case SubscriptionPlanTier.basic:
        defaults[EntitlementFeatureKeys.vrSessionControl] = true;
        defaults[EntitlementFeatureKeys.parentGuidedStart] = true;
        defaults[EntitlementFeatureKeys.progressInsights] = true;
        defaults[EntitlementFeatureKeys.rewardsUnlocks] = true;
        break;
      case SubscriptionPlanTier.premium:
        defaults[EntitlementFeatureKeys.vrSessionControl] = true;
        defaults[EntitlementFeatureKeys.parentGuidedStart] = true;
        defaults[EntitlementFeatureKeys.progressInsights] = true;
        defaults[EntitlementFeatureKeys.therapeuticStories] = true;
        defaults[EntitlementFeatureKeys.coloringPrintables] = true;
        defaults[EntitlementFeatureKeys.premiumModules] = true;
        defaults[EntitlementFeatureKeys.rewardsUnlocks] = true;
        break;
      case SubscriptionPlanTier.live:
        defaults[EntitlementFeatureKeys.vrSessionControl] = true;
        defaults[EntitlementFeatureKeys.parentGuidedStart] = true;
        defaults[EntitlementFeatureKeys.progressInsights] = true;
        defaults[EntitlementFeatureKeys.therapeuticStories] = true;
        defaults[EntitlementFeatureKeys.coloringPrintables] = true;
        defaults[EntitlementFeatureKeys.premiumModules] = true;
        defaults[EntitlementFeatureKeys.liveTherapistSupport] = true;
        defaults[EntitlementFeatureKeys.rewardsUnlocks] = true;
        break;
      case SubscriptionPlanTier.unknown:
        break;
    }

    return defaults;
  }

  static Map<String, bool> _parseFeatureFlags(dynamic rawFlags) {
    if (rawFlags is! Map<String, dynamic>) {
      return const <String, bool>{};
    }

    final parsed = <String, bool>{};
    for (final entry in rawFlags.entries) {
      final key = entry.key.trim();
      if (key.isEmpty) {
        continue;
      }
      final value = entry.value;
      if (value is bool) {
        parsed[key] = value;
      }
    }
    return parsed;
  }

  static Set<String> _parseAllowedGameIds(dynamic rawAllowedGameIds) {
    if (rawAllowedGameIds is! List<dynamic>) {
      return const <String>{};
    }

    final allowed = <String>{};
    for (final value in rawAllowedGameIds) {
      if (value is String && value.trim().isNotEmpty) {
        allowed.add(value.trim());
      }
    }
    return allowed;
  }

  static int _readInt(dynamic rawValue, int fallback) {
    if (rawValue is int) {
      return rawValue;
    }
    if (rawValue is num) {
      return rawValue.toInt();
    }
    if (rawValue is String) {
      final parsed = int.tryParse(rawValue.trim());
      if (parsed != null) {
        return parsed;
      }
    }
    return fallback;
  }
}

enum LicenseStatus {
  active,
  expired,
  revoked,
  none,
}

extension LicenseStatusCodec on LicenseStatus {
  static LicenseStatus fromWire(String? wireValue) {
    switch (wireValue?.trim().toUpperCase()) {
      case 'ACTIVE':
        return LicenseStatus.active;
      case 'EXPIRED':
        return LicenseStatus.expired;
      case 'REVOKED':
        return LicenseStatus.revoked;
      default:
        return LicenseStatus.none;
    }
  }

  String get wireValue {
    switch (this) {
      case LicenseStatus.active:
        return 'ACTIVE';
      case LicenseStatus.expired:
        return 'EXPIRED';
      case LicenseStatus.revoked:
        return 'REVOKED';
      case LicenseStatus.none:
        return 'NONE';
    }
  }
}

class LicenseGrant {
  final LicenseStatus status;
  final DateTime? fromUtc;
  final DateTime? toUtc;
  final bool perpetual;

  const LicenseGrant({
    required this.status,
    this.fromUtc,
    this.toUtc,
    this.perpetual = false,
  });

  factory LicenseGrant.fromMap(Map<String, dynamic>? data) {
    if (data == null) {
      return const LicenseGrant(status: LicenseStatus.none);
    }

    return LicenseGrant(
      status: LicenseStatusCodec.fromWire(data['status'] as String?),
      fromUtc: _toUtcDateTime(data['fromUtc']),
      toUtc: _toUtcDateTime(data['toUtc']),
      perpetual: data['perpetual'] as bool? ?? false,
    );
  }

  bool isActiveAt(DateTime atUtc) {
    if (status != LicenseStatus.active) {
      return false;
    }

    if (fromUtc != null && atUtc.isBefore(fromUtc!)) {
      return false;
    }

    if (!perpetual && toUtc != null && atUtc.isAfter(toUtc!)) {
      return false;
    }

    return true;
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'status': status.wireValue,
      'fromUtc': fromUtc?.toIso8601String(),
      'toUtc': toUtc?.toIso8601String(),
      'perpetual': perpetual,
    };
  }

  static DateTime? _toUtcDateTime(dynamic value) {
    if (value is Timestamp) {
      return value.toDate().toUtc();
    }

    if (value is DateTime) {
      return value.toUtc();
    }

    if (value is String) {
      return DateTime.tryParse(value)?.toUtc();
    }

    return null;
  }
}

class EntitlementAccess {
  final EntitlementRole role;
  final LicenseGrant appLicense;
  final Map<String, LicenseGrant> gameLicenses;
  final EntitlementPlanProfile planProfile;
  final String sourceTag;
  final String? policyVersion;

  const EntitlementAccess({
    required this.role,
    required this.appLicense,
    required this.gameLicenses,
    required this.planProfile,
    required this.sourceTag,
    this.policyVersion,
  });

  factory EntitlementAccess.fromBackend({
    required Map<String, dynamic> data,
    required String sourceTag,
  }) {
    final role = EntitlementRoleCodec.fromWire(data['role'] as String?);
    final rawGameLicenses = data['gameLicenses'];
    final parsedGameLicenses = <String, LicenseGrant>{};

    if (rawGameLicenses is Map<String, dynamic>) {
      for (final entry in rawGameLicenses.entries) {
        if (entry.value is Map<String, dynamic>) {
          parsedGameLicenses[entry.key] =
              LicenseGrant.fromMap(entry.value as Map<String, dynamic>);
        }
      }
    }

    final planPayload = _readPlanProfileFromPayload(data) ??
        _buildLegacyDefaultPlanProfilePayload(role);

    return EntitlementAccess(
      role: role,
      appLicense:
          LicenseGrant.fromMap(data['appLicense'] as Map<String, dynamic>?),
      gameLicenses: parsedGameLicenses,
      planProfile: EntitlementPlanProfile.fromMap(planPayload),
      sourceTag: sourceTag,
      policyVersion: data['policyVersion'] as String?,
    );
  }

  bool hasAppAccess(DateTime atUtc) => appLicense.isActiveAt(atUtc);

  bool hasGameAccess({
    required String gameId,
    required DateTime atUtc,
  }) {
    final grant = gameLicenses[gameId];
    if (grant == null) {
      return hasAppAccess(atUtc);
    }

    return grant.isActiveAt(atUtc);
  }

  bool isFeatureEnabled(String featureKey) {
    return planProfile.isFeatureEnabled(featureKey);
  }

  bool isGameAllowedByPlan(String gameId) {
    return planProfile.isGameAllowed(gameId);
  }

  Map<String, dynamic> toMap() {
    return <String, dynamic>{
      'role': role.wireValue,
      'appLicense': appLicense.toMap(),
      'gameLicenses':
          gameLicenses.map((key, value) => MapEntry(key, value.toMap())),
      'planProfile': planProfile.toMap(),
      'sourceTag': sourceTag,
      'policyVersion': policyVersion,
    };
  }

  static Map<String, dynamic>? _readPlanProfileFromPayload(
    Map<String, dynamic> data,
  ) {
    final rawPlanProfile = data['planProfile'];
    if (rawPlanProfile is Map<String, dynamic>) {
      return rawPlanProfile;
    }

    final hasLegacyPlanFields = data.containsKey('subscriptionPlan') ||
        data.containsKey('planTier') ||
        data.containsKey('featureFlags') ||
        data.containsKey('allowedGameIds') ||
        data.containsKey('demoSessionLimit') ||
        data.containsKey('demoSessionsUsed');
    if (!hasLegacyPlanFields) {
      return null;
    }

    return <String, dynamic>{
      'tier': data['planTier'] ?? data['subscriptionPlan'],
      'featureFlags': data['featureFlags'],
      'allowedGameIds': data['allowedGameIds'],
      'demoSessionLimit': data['demoSessionLimit'],
      'demoSessionsUsed': data['demoSessionsUsed'],
    };
  }

  static Map<String, dynamic> _buildLegacyDefaultPlanProfilePayload(
    EntitlementRole role,
  ) {
    final tier = switch (role) {
      EntitlementRole.parent => 'FREE',
      EntitlementRole.therapist => 'BASIC',
      EntitlementRole.unknown => 'UNKNOWN',
    };
    return <String, dynamic>{'tier': tier};
  }
}

class EntitlementGateDecision {
  final bool isAllowed;
  final String reasonCode;
  final String message;
  final EntitlementAccess access;
  final bool usedLegacyFallback;

  const EntitlementGateDecision({
    required this.isAllowed,
    required this.reasonCode,
    required this.message,
    required this.access,
    this.usedLegacyFallback = false,
  });
}
