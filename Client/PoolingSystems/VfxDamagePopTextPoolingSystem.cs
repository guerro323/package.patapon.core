using System;
using DefaultNamespace;
using package.patapon.core.Models.InGame.VFXDamage;
using StormiumTeam.GameBase;
using StormiumTeam.GameBase.Components;
using StormiumTeam.GameBase.Systems;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Patapon.Client.PoolingSystems
{
	[UpdateInWorld(UpdateInWorld.TargetWorld.Client)]
	[UpdateInGroup(typeof(OrderGroup.Simulation))]
	public class VfxDamagePopTextPoolingSystem : PoolingSystem<VfxDamagePopTextBackend, VfxDamagePopTextPresentation>
	{
		protected override Type[] AdditionalBackendComponents => new Type[] {typeof(SortingGroup)};
		
		protected override string AddressableAsset =>
			AddressBuilder.Client()
			              .Interface()
			              .Folder("InGame")
			              .Folder("Effects")
			              .Folder("VfxDamage")
			              .GetFile("VfxDamagePopTextDefault.prefab");

		protected override EntityQuery GetQuery()
		{
			return GetEntityQuery(typeof(TargetDamageEvent));
		}

		protected override void SpawnBackend(Entity target)
		{
			base.SpawnBackend(target);

			LastBackend.Play(EntityManager.GetComponentData<TargetDamageEvent>(LastBackend.DstEntity));
			LastBackend.setToPoolAt = Time.ElapsedTime + 2f;
			LastBackend.transform.localScale = Vector3.one * 0.5f;
			
			var sortingGroup = LastBackend.GetComponent<SortingGroup>();
			sortingGroup.sortingLayerName = "OverlayUI";
		}

		protected override void ReturnBackend(VfxDamagePopTextBackend backend)
		{
			if (backend.setToPoolAt > Time.ElapsedTime)
				return;
			base.ReturnBackend(backend);
		}
	}
}