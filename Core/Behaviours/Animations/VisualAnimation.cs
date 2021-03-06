using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PataNext.Client.Graphics.Animation
{
	public class PlayableBehaviorData
	{
		public Entity        DstEntity;
		public EntityManager DstEntityManager;
	}

	public struct Transition
	{
		public float Key0;
		public float Key1;
		public float Key2;
		public float Key3;

		public Transition(AnimationClip clip, float pg2, float pg3)
		{
			Key0 = 0;
			Key1 = 0;
			Key2 = clip.length * pg2;
			Key3 = clip.length * pg3;
		}

		public Transition(AnimationClip clip, float pg0, float pg1, float pg2, float pg3)
		{
			Key0 = clip.length * pg0;
			Key1 = clip.length * pg1;
			Key2 = clip.length * pg2;
			Key3 = clip.length * pg3;
		}

		public Transition(Transition left, float pg2, float pg3)
		{
			Key0 = left.Key2;
			Key1 = left.Key3;
			Key2 = pg2 + Key0;
			Key3 = pg3 + Key0;
		}

		public Transition(Transition left, AnimationClip clip, float pg2, float pg3)
		{
			Key0 = left.Key2;
			Key1 = left.Key3;
			Key2 = clip.length * pg2 + Key0;
			Key3 = clip.length * pg3 + Key0;
		}

		public void Begin(float key0, float key1)
		{
			Key0 = key0;
			Key1 = key1;
		}

		public void End(float key2, float key3)
		{
			Key2 = key2;
			Key3 = key3;
		}

		public float Evaluate(float time, float left = 0, float right = 0)
		{
			if (time > Key3)
				return right;
			if (time <= Key0)
				return left;
			if (time <= Key1)
				return math.max(math.unlerp(Key0, Key1, time), left);
			if (time >= Key2 && time <= Key3)
				return math.max(math.unlerp(Key3, Key2, time), right);
			return 1;
		}
	}

	public class VisualAnimationPlayable : PlayableBehaviour
	{
		public  Playable               Playable;
		public  AnimationMixerPlayable RootMixer;
		private PlayableGraph          graph => Playable.GetGraph();

		public override void OnPlayableCreate(Playable playable)
		{
			Playable = playable;
			Playable.SetInputCount(1);

			RootMixer = AnimationMixerPlayable.Create(graph);
			RootMixer.SetTraversalMode(PlayableTraversalMode.Mix);
			graph.Connect(RootMixer, 0, Playable, 0);

			Playable.SetInputWeight(0, 1);
		}
	}

	public class VisualAnimation : MonoBehaviour
	{
		public delegate void AddSystem<T>(ref ManageData data, ref T systemData) where T : struct;

		public delegate void RemoveSystem<in T>(ManageData data, T systemData) where T : struct;

		private   AnimationPlayableOutput m_AnimationPlayableOutput;
		protected VisualAnimationPlayable m_Playable;
		protected PlayableGraph           m_PlayableGraph;

		public PlayableGraph Graph => m_PlayableGraph;

		protected Dictionary<Type, SystemDataBase> m_SystemData = new Dictionary<Type, SystemDataBase>();
		protected AnimationMixerPlayable           rootMixer => m_Playable.RootMixer;


		public void DestroyPlayableGraph()
		{
			if (m_PlayableGraph.IsValid())
				m_PlayableGraph.Destroy();
			m_SystemData.Clear();
		}

		private void OnDestroy()
		{
			DestroyPlayableGraph();
		}

		public void CreatePlayableGraph(string name)
		{
			m_PlayableGraph = PlayableGraph.Create($"{GetType()}.{name}");
		}

		public void CreatePlayable(Animator animator = null)
		{
			m_PlayableGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
			m_PlayableGraph.Play();
			m_Playable                = ScriptPlayable<VisualAnimationPlayable>.Create(m_PlayableGraph).GetBehaviour();
			m_AnimationPlayableOutput = AnimationPlayableOutput.Create(m_PlayableGraph, "Output", animator);
			m_AnimationPlayableOutput.SetSourcePlayable(m_Playable.Playable, 0);
		}

		public void SetAnimatorOutput(string outputName, Animator animator)
		{
			if (m_AnimationPlayableOutput.GetHandle() == null)
				throw new InvalidOperationException();

			m_AnimationPlayableOutput.SetTarget(animator);
			animator.runtimeAnimatorController = null;
		}

		public bool ContainsSystem(Type type)
		{
			return m_SystemData.ContainsKey(type);
		}

		public void InsertSystem<T>(Type type, AddSystem<T> addDelegate, RemoveSystem<T> removeDelegate)
			where T : struct
		{
			var data = new ManageData
			{
				Handle   = this,
				Behavior = m_Playable,
				Index    = m_SystemData.Count,
				Graph    = m_PlayableGraph
			};
			m_SystemData[type] = new SystemData<T>
			{
				Data           = new T(),
				Index          = m_SystemData.Count,
				Type           = type,
				RemoveDelegate = removeDelegate
			};

			addDelegate(ref data, ref ((SystemData<T>) m_SystemData[type]).Data);
		}

		public ref T GetSystemData<T>(Type type)
			where T : struct
		{
			return ref ((SystemData<T>) m_SystemData[type]).Data;
		}

		public static int GetIndexFrom(Playable parent, Playable child)
		{
			var rootInputCount = parent.GetInputCount();
			for (var i = 0; i != rootInputCount; i++)
				if (parent.GetInput(i).Equals(child))
					return i;

			return -1;
		}

		public static float GetWeightFixed(double time, double start, double end)
		{
			if (start < 0 || end < 0)
				return 0;
			if (time > end)
				return 0;
			if (time < start)
				return 1;
			return (float) (1 - math.unlerp(start, end, time));
		}

		protected abstract class SystemDataBase
		{
			public int  Index;
			public Type Type;
		}

		protected class SystemData<T> : SystemDataBase
			where T : struct
		{
			public T               Data;
			public RemoveSystem<T> RemoveDelegate;
		}

		public struct ManageData
		{
			public VisualAnimation         Handle;
			public PlayableGraph           Graph;
			public VisualAnimationPlayable Behavior;
			public int                     Index;
		}
	}
}