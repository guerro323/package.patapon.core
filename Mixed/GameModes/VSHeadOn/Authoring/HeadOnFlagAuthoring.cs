using System;
using Revolution;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine;

namespace Patapon.Mixed.GameModes.VSHeadOn
{
	public class HeadOnFlagAuthoring : MonoBehaviour, IConvertGameObjectToEntity
	{
		public HeadOnDefineTeamAuthoring TeamDefine;

		public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
		{
			Debug.Assert(TeamDefine != null, "TeamDefine != null");

			dstManager.AddComponentData(entity, new HeadOnFlag());
			dstManager.AddComponentData(entity, TeamDefine.FindOrCreate(dstManager));
			dstManager.AddComponent(entity, typeof(GhostEntity));
		}

		private void OnDrawGizmos()
		{
			Color color;
			if (TeamDefine != null)
				switch (TeamDefine.PredefinedTeam)
				{
					case EHeadOnTeamTarget.Blue:
						color = Color.blue;
						break;
					case EHeadOnTeamTarget.Red:
						color = Color.red;
						break;
					case EHeadOnTeamTarget.Undefined:
						color = Color.green;
						break;
					default:
						throw new ArgumentOutOfRangeException();
				}
			else
				color = Color.black;

			Gizmos.color = Color.Lerp(color, Color.white, 0.33f);
			Gizmos.DrawWireSphere(transform.position, 0.25f);
		}
	}

	public struct HeadOnFlag : IComponentData
	{
		public int foo;
		
		public class Synchronize : ComponentSnapshotSystemTag<HeadOnFlag>
		{
		}
	}
}