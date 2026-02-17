using UnityEngine;
using VContainer;
using VContainer.Unity;
using TheraplyCore.Connection;
using TheraplyCore.Firebase;
using Logger = TheraplyCore.Logging.Logger;

public class DemoLifetimeScope : LifetimeScope
{
    [SerializeField] private ConnectionStateManager _connectionManager;
    [SerializeField] private ReliableCommandService _reliableCommand;
    [SerializeField] private FirebaseDataService _firebaseData;
    
    protected override void Configure(IContainerBuilder builder)
    {
        // Register services as instances (singletons)
        builder.RegisterInstance(_connectionManager);
        builder.RegisterInstance(_reliableCommand);
        builder.RegisterInstance(_firebaseData);
        
        Logger.Info("[VContainer] Core services registered");
    }
}