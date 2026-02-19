using UnityEngine;
using VContainer;
using VContainer.Unity;
using TheraplyCore.Firebase;
using Logger = TheraplyCore.Logging.Logger;

public class DemoLifetimeScope : LifetimeScope
{
    [SerializeField] private FirebaseDataService _firebaseData;

    protected override void Configure(IContainerBuilder builder)
    {
        if (_firebaseData != null)
        {
            builder.RegisterInstance(_firebaseData);
        }

        Logger.Info("[VContainer] DemoLifetimeScope configured");
    }
}
