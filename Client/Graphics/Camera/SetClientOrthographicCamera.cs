using package.stormiumteam.shared.ecs;
using StormiumTeam.GameBase.Utility.DOTS.xMonoBehaviour;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace PataNext.Client.Graphics.Camera
{
	public class SetClientOrthographicCamera : ComponentSystem
	{
		private EntityQuery m_Query;

		protected override void OnCreate()
		{
			m_Query = GetEntityQuery(typeof(GameCamera), typeof(UnityEngine.Camera), ComponentType.Exclude<IsActive>());
		}

		protected override void OnUpdate()
		{
			Entities.With(m_Query).ForEach((Entity e, UnityEngine.Camera camera) =>
			{
				camera.orthographicSize = 10;
				camera.orthographic     = true;

				camera.transform.position = new Vector3(0, 0, -100);

				EntityManager.SetOrAddComponentData(e, new CameraTargetAnchor
				{
					Type  = AnchorType.Screen,
					Value = new float2(0, 0.9f)
				});
				EntityManager.SetOrAddComponentData(e, new AnchorOrthographicCameraData());
				EntityManager.SetOrAddComponentData(e, new AnchorOrthographicCameraOutput());
				EntityManager.SetOrAddComponentData(e, new IsActive());
			});
		}

		public struct IsActive : IComponentData
		{
		}
	}
}