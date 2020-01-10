using System;
using Discord;
using P4TLB.MasterServer;
using Patapon4TLB.Core.MasterServer;
using StormiumTeam.GameBase;
using StormiumTeam.GameBase.External.Discord;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Patapon4TLB.Core
{
	public class P4DiscordSystem : BaseDiscordSystem
	{
		protected override void OnCreate()
		{
			base.OnCreate();

			Push(default);
		}

		protected override void OnUpdate()
		{
			base.OnUpdate();

			if (!IsUserReady)
				return;
		}

		protected override long  ClientId                   => 609427243395055616;
		public             bool  IsConnectionLobbyRequested { get; set; }
		public             Lobby ConnectionLobby            { get; set; }

		public void CreateConnectionLobby()
		{
			IsConnectionLobbyRequested = true;
			var lobbyManager = GetDiscord().GetLobbyManager();
			var transaction  = lobbyManager.GetLobbyCreateTransaction();
			transaction.SetCapacity(2);
			transaction.SetMetadata($"{GetLocalUser().Id}", "0");
			transaction.SetType(LobbyType.Public);
			transaction.SetLocked(false);

			Debug.Log($"{GetLocalUser().Id} \"flag\"");

			lobbyManager.CreateLobby(transaction, (Result result, ref Lobby lobby) =>
			{
				IsConnectionLobbyRequested = false;
				ConnectionLobby            = lobby;
				Debug.Log("Connection lobby created!");
			});
		}

		public void DeleteConnectionLobby()
		{
			GetDiscord()
				.GetLobbyManager()
				.DeleteLobby(ConnectionLobby.Id, result => { Debug.Log("Connection lobby result deletion: " + result); });
			ConnectionLobby = default;
		}
	}

	[UpdateInWorld(UpdateInWorld.TargetWorld.Client)]
	[AlwaysUpdateSystem]
	public class P4ConnectToMasterServerFromDiscord : GameBaseSystem
	{
		private bool m_HasPendingRequest;

		public bool IsCurrentlyRequesting => m_HasPendingRequest;
		
		protected override void OnCreate()
		{
			base.OnCreate();
			if (IsServer)
				throw new NotImplementedException();
		}


		protected override void OnUpdate()
		{
			if (!(BaseDiscordSystem.Instance is P4DiscordSystem discordSystem))
				return;

			if (!m_HasPendingRequest || discordSystem.ConnectionLobby.Id == 0)
				return;
			
			var localUser = discordSystem.GetLocalUser();
			var request   = EntityManager.CreateEntity(typeof(RequestUserLogin));
			{
				EntityManager.SetComponentData(request, new RequestUserLogin
				{
					Login          = $"DISCORD_{localUser.Id}",
					HashedPassword = string.Empty,
					Type           = UserLoginRequest.Types.RequestType.Player,
					RoutedData     = new NativeString512("{\"lobby_id\"=\"" + discordSystem.ConnectionLobby.Id + "\"}")
				});
			}
			
			Debug.Log($"Sending request from {localUser.Username}#{localUser.Discriminator}");

			m_HasPendingRequest = false;
		}

		public void Request()
		{
			if (!(BaseDiscordSystem.Instance is P4DiscordSystem discordSystem))
				return;

			m_HasPendingRequest = true;
			if (discordSystem.ConnectionLobby.Id != 0)
				throw new Exception("A request already exist...");

			if (!discordSystem.IsConnectionLobbyRequested)
				discordSystem.CreateConnectionLobby();
		}
	}
}