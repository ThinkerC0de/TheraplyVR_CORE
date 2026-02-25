using System;

namespace TheraplyCore.Games.Contracts
{
    public static class GameCatalogDeliveryModes
    {
        public const string Bundled = "bundled";
        public const string OnDemand = "on_demand";

        public static bool IsSupported(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return string.Equals(value.Trim(), Bundled, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value.Trim(), OnDemand, StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeOrDefault(string value)
        {
            if (string.Equals(value?.Trim(), OnDemand, StringComparison.OrdinalIgnoreCase))
            {
                return OnDemand;
            }

            return Bundled;
        }
    }

    public static class GameCatalogContractReasonCodes
    {
        public const string Valid = "GAME_CATALOG_VALID";
        public const string GameIdRequired = "GAME_CATALOG_GAME_ID_REQUIRED";
        public const string TitleRequired = "GAME_CATALOG_TITLE_REQUIRED";
        public const string SceneKeyRequired = "GAME_CATALOG_SCENE_KEY_REQUIRED";
        public const string ContentVersionRequired = "GAME_CATALOG_CONTENT_VERSION_REQUIRED";
        public const string EntitlementKeyRequired = "GAME_CATALOG_ENTITLEMENT_KEY_REQUIRED";
        public const string DeliveryModeUnsupported = "GAME_CATALOG_DELIVERY_MODE_UNSUPPORTED";
    }

    [Serializable]
    public sealed class GameCatalogContractEntry
    {
        public string gameId = string.Empty;
        public string title = string.Empty;
        public string description = string.Empty;
        public string sceneKey = string.Empty;
        public string targetContentVersion = "1.0.0";
        public string contentVersion = string.Empty;
        public string entitlementKey = string.Empty;
        public string deliveryMode = GameCatalogDeliveryModes.Bundled;
        public string parameterSchema = string.Empty;
        public bool runtimeLaunchEnabled = true;

        public void Normalize()
        {
            gameId = NormalizeString(gameId);
            title = NormalizeString(title);
            description = NormalizeString(description);

            sceneKey = NormalizeString(sceneKey);
            if (string.IsNullOrWhiteSpace(sceneKey))
            {
                sceneKey = gameId;
            }

            targetContentVersion = NormalizeString(targetContentVersion);
            if (string.IsNullOrWhiteSpace(targetContentVersion))
            {
                targetContentVersion = "1.0.0";
            }

            contentVersion = NormalizeString(contentVersion);
            if (string.IsNullOrWhiteSpace(contentVersion))
            {
                contentVersion = targetContentVersion;
            }

            entitlementKey = NormalizeString(entitlementKey);
            if (string.IsNullOrWhiteSpace(entitlementKey))
            {
                entitlementKey = $"game:{gameId}";
            }

            deliveryMode = NormalizeString(deliveryMode).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(deliveryMode))
            {
                deliveryMode = GameCatalogDeliveryModes.Bundled;
            }
            parameterSchema = NormalizeString(parameterSchema);
        }

        public bool TryValidate(out string reasonCode)
        {
            Normalize();

            if (string.IsNullOrWhiteSpace(gameId))
            {
                reasonCode = GameCatalogContractReasonCodes.GameIdRequired;
                return false;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                reasonCode = GameCatalogContractReasonCodes.TitleRequired;
                return false;
            }

            if (string.IsNullOrWhiteSpace(sceneKey))
            {
                reasonCode = GameCatalogContractReasonCodes.SceneKeyRequired;
                return false;
            }

            if (string.IsNullOrWhiteSpace(contentVersion))
            {
                reasonCode = GameCatalogContractReasonCodes.ContentVersionRequired;
                return false;
            }

            if (string.IsNullOrWhiteSpace(entitlementKey))
            {
                reasonCode = GameCatalogContractReasonCodes.EntitlementKeyRequired;
                return false;
            }

            if (!GameCatalogDeliveryModes.IsSupported(deliveryMode))
            {
                reasonCode = GameCatalogContractReasonCodes.DeliveryModeUnsupported;
                return false;
            }

            reasonCode = GameCatalogContractReasonCodes.Valid;
            return true;
        }

        public static GameCatalogContractEntry CreateDefault(string gameId, string title)
        {
            var normalizedGameId = NormalizeString(gameId);
            return new GameCatalogContractEntry
            {
                gameId = normalizedGameId,
                title = NormalizeString(title),
                description = string.Empty,
                sceneKey = normalizedGameId,
                targetContentVersion = "1.0.0",
                contentVersion = "1.0.0",
                entitlementKey = $"game:{normalizedGameId}",
                deliveryMode = GameCatalogDeliveryModes.Bundled,
                parameterSchema = string.Empty,
                runtimeLaunchEnabled = true,
            };
        }

        private static string NormalizeString(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
        }
    }
}
