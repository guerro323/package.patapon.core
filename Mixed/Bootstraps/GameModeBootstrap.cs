using ENet;
using P4TLB.MasterServer;
using package.stormiumteam.shared.ecs;
using Patapon.Mixed.GameModes;
using Patapon.Mixed.GameModes.VSHeadOn;
using Patapon.Mixed.Units;
using Patapon.Mixed.Units.Statistics;
using Patapon4TLB.Core;
using Patapon4TLB.Default;
using Patapon4TLB.Default.Player;
using Revolution;
using StormiumTeam.GameBase;
using StormiumTeam.GameBase.Bootstraping;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using Unity.Networking.Transport;
using UnityEngine;

namespace Bootstraps
{
	[UpdateInWorld(UpdateInWorld.TargetWorld.Default)]
	public class GameModeBootstrap : BaseBootstrapSystem
	{
		public int CreationType = 0;
		
		protected override void Register(Entity bootstrap)
		{
			EntityManager.SetComponentData(bootstrap, new BootstrapComponent {Name = nameof(GameModeBootstrap)});
		}

		protected override void Match(Entity bootstrapSingleton)
		{
			if (CreationType == 0)
			{
				if (Input.GetKeyDown(KeyCode.H))
					CreationType = 1;
				if (Input.GetKeyDown(KeyCode.C))
					CreationType = 2;
				return;
			}

			foreach (var world in World.AllWorlds)
			{
				var network = world.GetExistingSystem<NetworkStreamReceiveSystem>();
				if (world.GetExistingSystem<SimpleRhythmTestSystem>() != null)
				{
					var ent = world.EntityManager.CreateEntity(typeof(IsActive));
					world.EntityManager.SetComponentData(ent, new IsActive {RequiredPlayers = 1});
				}

				if (world.GetExistingSystem<ClientSimulationSystemGroup>() != null && CreationType >= 1)
				{
					// Client worlds automatically connect to localhost
					var ep = new Address();
					ep.SetIP("127.0.0.1");
					ep.Port = 7979;
					network.Connect(ep);
				}
				else if (world.GetExistingSystem<ServerSimulationSystemGroup>() != null && CreationType == 1)
				{
					// Server world automatically listen for connections from any host
					var ep = new Address();
					ep.Port = 7979;
					network.Listen(ep);
				}
			}

			EntityManager.DestroyEntity(bootstrapSingleton);
		}

		public struct IsActive : IComponentData
		{
			public int RequiredPlayers;
		}
	}

	[UpdateInGroup(typeof(ClientAndServerSimulationSystemGroup))]
	public class GameModeBootstrapTestSystem : GameBaseSystem
	{
		private bool        m_Created;
		private EntityQuery m_PlayerQuery;

		protected override void OnCreate()
		{
			base.OnCreate();

			m_PlayerQuery = GetEntityQuery(typeof(GamePlayer), typeof(GamePlayerReadyTag));

			RequireSingletonForUpdate<GameModeBootstrap.IsActive>();
		}

		private void SingleTeam()
		{
			var playerEntities = m_PlayerQuery.ToEntityArray(Allocator.TempJob);
			var count = playerEntities.Length;
			count += 3;

			// Create formation
			const int formationCount = 2;
			for (var _ = 0; _ != formationCount; _++)
			{
				/*if (_ == 1)
					break;*/
				
				var formationRoot = EntityManager.CreateEntity(typeof(GameFormationTag), typeof(FormationTeam), typeof(FormationRoot));
				{
					for (var i = 0; i != count; i++)
					{
						/*if (playerEntities[i] == default)
							continue;*/
						
						var armyEntity = EntityManager.CreateEntity(typeof(ArmyFormation), typeof(FormationParent), typeof(FormationChild));
						EntityManager.SetComponentData(armyEntity, new FormationParent {Value = formationRoot});

						var unitEntity = EntityManager.CreateEntity(typeof(UnitFormation), typeof(UnitStatistics), typeof(UnitDefinedAbilities), typeof(FormationParent));
						EntityManager.SetComponentData(unitEntity, new FormationParent {Value = armyEntity});

						var statistics = new UnitStatistics
						{
							Health  = 1000,
							Attack  = 24,
							Defense = 7,

							BaseWalkSpeed       = 2f,
							FeverWalkSpeed      = 2.2f,
							AttackSpeed         = 2.0f,
							MovementAttackSpeed = 3.1f,
							Weight              = 8.5f,
							AttackSeekRange     = 16f
						};
						
						var displayEquipment = new UnitDisplayedEquipment();
						var targetKit        = UnitKnownTypes.Yarida;
						if (playerEntities.Length > i && playerEntities[i] != Entity.Null)
						{
							targetKit = UnitKnownTypes.Yarida;
							EntityManager.ReplaceOwnerData(unitEntity, playerEntities[i]);
						}
						else
						{
							targetKit = UnitKnownTypes.Taterazay;

							// create a fake player
							var playerArchetype = World.GetExistingSystem<GamePlayerProvider>().EntityArchetype;
							var playerEntity    = EntityManager.CreateEntity(playerArchetype);

							EntityManager.AddComponent(playerEntity, typeof(GamePlayerReadyTag));
							EntityManager.AddComponent(playerEntity, typeof(GhostEntity));
							EntityManager.ReplaceOwnerData(unitEntity, playerEntity);
						}

						var definedAbilities = EntityManager.GetBuffer<UnitDefinedAbilities>(unitEntity); 
						KitTempUtility.Set(targetKit, ref statistics, definedAbilities, ref displayEquipment);

						EntityManager.SetOrAddComponentData(unitEntity, new UnitCurrentKit {Value = targetKit});
						EntityManager.SetOrAddComponentData(unitEntity, statistics);
						EntityManager.SetOrAddComponentData(unitEntity, displayEquipment);

						if (playerEntities.Length > i) playerEntities[i] = Entity.Null;
					}
				}

				EntityManager.SetComponentData(formationRoot, new FormationTeam {TeamIndex = _ + 1});
			}

			playerEntities.Dispose();
		}

		private void FaceToFace()
		{
			var playerEntities = m_PlayerQuery.ToEntityArray(Allocator.TempJob);
			var count          = playerEntities.Length;
			var teamSize       = count / 2;
			var playerIndex    = 0;

			// Create formation
			const int formationCount = 2;
			for (var _ = 0; _ != formationCount; _++)
			{
				var formationRoot = EntityManager.CreateEntity(typeof(GameFormationTag), typeof(FormationTeam), typeof(FormationRoot));
				{
					for (var i = 0; i != teamSize; i++)
					{
						if (playerEntities[playerIndex] == default)
							continue;

						var armyEntity = EntityManager.CreateEntity(typeof(ArmyFormation), typeof(FormationParent), typeof(FormationChild));
						EntityManager.SetComponentData(armyEntity, new FormationParent {Value = formationRoot});

						var unitEntity = EntityManager.CreateEntity(typeof(UnitFormation), typeof(UnitStatistics), typeof(UnitDefinedAbilities), typeof(FormationParent));
						EntityManager.SetComponentData(unitEntity, new FormationParent {Value = armyEntity});

						var statistics = new UnitStatistics
						{
							Health  = 1000,
							Attack  = 24,
							Defense = 7,

							BaseWalkSpeed       = 2f,
							FeverWalkSpeed      = 2.2f,
							AttackSpeed         = 2.0f,
							MovementAttackSpeed = 3.1f,
							Weight              = 8.5f,
							AttackSeekRange     = 16f
						};

						var displayEquipment = new UnitDisplayedEquipment();
						var targetKit        = UnitKnownTypes.Taterazay;
						if (playerEntities.Length > playerIndex && playerEntities[playerIndex] != Entity.Null)
						{
							targetKit = UnitKnownTypes.Taterazay;
							EntityManager.ReplaceOwnerData(unitEntity, playerEntities[playerIndex]);
						}
						else
						{
							targetKit = UnitKnownTypes.Taterazay;

							// create a fake player
							var playerArchetype = World.GetExistingSystem<GamePlayerProvider>().EntityArchetype;
							var playerEntity    = EntityManager.CreateEntity(playerArchetype);

							EntityManager.AddComponent(playerEntity, typeof(GamePlayerReadyTag));
							EntityManager.AddComponent(playerEntity, typeof(GhostEntity));
							EntityManager.ReplaceOwnerData(unitEntity, playerEntity);
						}

						var definedAbilities = EntityManager.GetBuffer<UnitDefinedAbilities>(unitEntity);
						KitTempUtility.Set(targetKit, ref statistics, definedAbilities, ref displayEquipment);

						EntityManager.SetOrAddComponentData(unitEntity, new UnitCurrentKit {Value = targetKit});
						EntityManager.SetOrAddComponentData(unitEntity, statistics);
						EntityManager.SetOrAddComponentData(unitEntity, displayEquipment);

						if (playerEntities.Length > playerIndex) playerEntities[playerIndex] = Entity.Null;
					}

					playerIndex++;
				}

				EntityManager.SetComponentData(formationRoot, new FormationTeam {TeamIndex = _ + 1});
			}

			playerEntities.Dispose();
		}

		protected override void OnUpdate()
		{
			if (!IsServer || m_Created)
				return;

			if (m_PlayerQuery.CalculateEntityCount() != GetSingleton<GameModeBootstrap.IsActive>().RequiredPlayers)
				return;

			m_Created = true;
			
			SingleTeam();

			// START THE GAMEMODE
			var gamemodeMgr = World.GetOrCreateSystem<GameModeManager>();
			gamemodeMgr.SetGameMode(new MpVersusHeadOn());
		}
	}
}