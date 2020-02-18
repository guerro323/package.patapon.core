using Unity.Mathematics;

namespace Patapon.Mixed.GamePlay.Physics
{
	public static class PredictTrajectory
	{
		public static float3 Simple(float3 start, float3 velocity, float3 gravity, float delta = 0.05f, int iteration = 50, float yLimit = 0)
		{
			for (var i = 0; i < iteration; i++)
			{
				velocity += gravity * delta;
				start += velocity * delta;
				
				if (yLimit >= start.y)
					break;
			}

			return start;
		}

		public static float3 GetDisplacement(float3 velocity, float3 gravity, float time)
		{
			return velocity * time + (gravity * math.pow(time, 2)) / 2;
		}
	}
}