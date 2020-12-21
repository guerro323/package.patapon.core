using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using PataNext.Client.Asset;
using StormiumTeam.GameBase.Utility.AssetBackend;
using StormiumTeam.GameBase.Utility.Misc;
using StormiumTeam.GameBase.Utility.Pooling;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Events;

namespace PataNext.Client.Behaviors
{
	public interface IContainer<T> : IDisposable
	{
		void    SetSize(int size);
		UniTask Warm();

		abstract UniTask<(T element, int index)> Add();

		abstract UnityEvent<(T element, int index)>             onAdded            { get; }
		abstract UnityEvent<World.NoAllocReadOnlyCollection<T>> onCollectionUpdate { get; }
		abstract UnityEvent<(T element, int index)>             onRemoved          { get; }

		abstract World.NoAllocReadOnlyCollection<T> GetList();
	}

	public static class ContainerPool
	{
		public static BackendContainerPool<TBackend, TPresentation> FromPresentation<TBackend, TPresentation>(AssetPath assetPath) 
			where TBackend : RuntimeAssetBackendBase 
			where TPresentation : RuntimeAssetPresentation
		{
			return new BackendContainerPool<TBackend, TPresentation>(new AsyncAssetPool<GameObject>(assetPath));
		}
		
		public static BackendContainerPool<TBackend, TPresentation> FromPresentation<TBackend, TPresentation>(GameObject reference) 
			where TBackend : RuntimeAssetBackendBase 
			where TPresentation : RuntimeAssetPresentation
		{
			return new BackendContainerPool<TBackend, TPresentation>(reference);
		}

		public static ContainerPool<TAsset> FromGameObject<TAsset>(GameObject reference)
			where TAsset : Component
		{
			// throw an exception to say to the user to call FromPresentation('')
			if (reference.GetComponent<RuntimeAssetPresentation>())
			{
				throw new InvalidOperationException($"Use FromPresentation<TBackend, {typeof(TAsset).Name}>(reference)");
			}
			
			return new ContainerPool<TAsset>(new CastAssetPool<GameObject, TAsset>(new AsyncAssetPool<GameObject>(reference),
				asset => asset.gameObject,
				go =>
				{
					if (!go.TryGetComponent(out TAsset asset))
						throw new InvalidOperationException($"GameObject {go.name} does not have any {typeof(TAsset).Name} component!");

					return asset;
				}));
		}
	}

	public class BackendContainerPool<TBackend, TPresentation> : IContainer<TPresentation>
		where TBackend : RuntimeAssetBackendBase
		where TPresentation : RuntimeAssetPresentation
	{
		private AssetPool<GameObject> backendPool;
		
		public ContainerPool<TPresentation> Backing;

		public BackendContainerPool(AsyncAssetPool<GameObject> presentationPool)
		{
			backendPool = new AssetPool<GameObject>(pool =>
			{
				var go = new GameObject($"pooled={typeof(TBackend).Name}");
				go.SetActive(false);

				go.AddComponent<TBackend>();
				go.AddComponent<GameObjectEntity>();
				return go;
			});
			
			Backing = new ContainerPool<TPresentation>(new CastAssetPool<GameObject, TPresentation>(presentationPool,
				asset => asset.gameObject,
				go =>
				{
					if (!go.TryGetComponent(out TPresentation asset))
						throw new InvalidOperationException($"GameObject {go.name} does not have any {typeof(TPresentation).Name} component!");

					return asset;
				}));

			Backing.onAdded.AddListener(args =>
			{
				var (element, _) = args;

				var gameObject = backendPool.Dequeue();
				gameObject.SetActive(true);
				
				var backend = gameObject.GetComponent<TBackend>();
				backend.OnReset();
				backend.SetTarget(World.DefaultGameObjectInjectionWorld.EntityManager);
				backend.SetPresentationSingle(element.gameObject);
			});
		}
		
		public BackendContainerPool(GameObject reference)
			: this(new AsyncAssetPool<GameObject>(reference))
		{
		}
		
		public void Dispose()
		{
			Backing.Dispose();
		}

		public void SetSize(int size)
		{
			Backing.SetSize(size);
		}

		public UniTask Warm()
		{
			return UniTask.WhenAll(Backing.Warm(), backendPool.Warm());
		}

		public UniTask<(TPresentation element, int index)> Add()
		{
			return Backing.Add();
		}

		public UnityEvent<(TPresentation element, int index)>             onAdded            => Backing.onAdded;
		public UnityEvent<World.NoAllocReadOnlyCollection<TPresentation>> onCollectionUpdate => Backing.onCollectionUpdate;
		public UnityEvent<(TPresentation element, int index)>             onRemoved          => Backing.onRemoved;

		public World.NoAllocReadOnlyCollection<TPresentation> GetList()
		{
			return Backing.GetList();
		}
	}

	public class ContainerPool<TAsset> : IContainer<TAsset>,
	                                     IDisposable
	{
		private readonly List<TAsset> activeObjects;

		public readonly IAssetPool<TAsset> AssetPool;

		public UnityEvent<(TAsset element, int index)>             onAdded            { get; }
		public UnityEvent<World.NoAllocReadOnlyCollection<TAsset>> onCollectionUpdate { get; }
		public UnityEvent<(TAsset element, int index)>             onRemoved          { get; }

		public ContainerPool(IAssetPool<TAsset> pool)
		{
			AssetPool = pool ?? throw new NullReferenceException(nameof(pool));

			activeObjects = new List<TAsset>();

			onAdded            = new UnityEvent<(TAsset element, int index)>();
			onCollectionUpdate = new UnityEvent<World.NoAllocReadOnlyCollection<TAsset>>();
			onRemoved          = new UnityEvent<(TAsset element, int index)>();
		}

		public void SetSize(int count)
		{
			if (activeObjects.Count == count)
				return;

			if (count > activeObjects.Count)
			{
				for (var i = activeObjects.Count; i < count; i++)
				{
					Add();
				}
			}
			else
			{
				for (var i = count; i < activeObjects.Count; i++)
				{
					AssetPool.Enqueue(activeObjects[i]);
				}

				activeObjects.RemoveRange(count, activeObjects.Count - count);
				
				onCollectionUpdate.Invoke(new World.NoAllocReadOnlyCollection<TAsset>(activeObjects));
			}
		}

		public UniTask Warm()
		{
			return AssetPool.Warm();
		}

		public async UniTask<(TAsset element, int index)> Add()
		{
			var completionSource = new UniTaskCompletionSource<TAsset>();
			var idx              = activeObjects.Count - 1;
			AssetPool.Dequeue(obj =>
			{
				activeObjects.Add(obj);
				completionSource.TrySetResult(obj);
			});
			var result = await completionSource.Task;

			var tuple = (result, activeObjects.Count - 1);
			onAdded.Invoke(tuple);
			
			onCollectionUpdate.Invoke(new World.NoAllocReadOnlyCollection<TAsset>(activeObjects));

			return tuple;
		}

		public World.NoAllocReadOnlyCollection<TAsset> GetList()
		{
			return new World.NoAllocReadOnlyCollection<TAsset>(activeObjects);
		}

		public void Dispose()
		{
			onAdded.RemoveAllListeners();
			onCollectionUpdate.RemoveAllListeners();
			onRemoved.RemoveAllListeners();

			SetSize(0);
			AssetPool.Dispose();
		}
	}

	internal class CastAssetPool<TIn, TTo> : IAssetPool<TTo>
	{
		public readonly IAssetPool<TIn> Source;
		public readonly Func<TIn, TTo>  CastToFunc;
		public readonly Func<TTo, TIn>  CastFromFunc;

		public CastAssetPool(IAssetPool<TIn> source, Func<TTo, TIn> from, Func<TIn, TTo> to)
		{
			Source       = source;
			CastFromFunc = from;
			CastToFunc   = to;
		}

		public UniTask Warm()
		{
			return Source.Warm();
		}
		
		public void Enqueue(TTo obj)
		{
			Source.Enqueue(CastFromFunc(obj));
		}

		public void Dequeue(OnAssetLoaded<TTo> onComplete)
		{
			// how to remove closure alloc here?
			Source.Dequeue(original => onComplete(CastToFunc(original)));
		}

		public void Dispose()
		{
			Source?.Dispose();
		}
	}
}