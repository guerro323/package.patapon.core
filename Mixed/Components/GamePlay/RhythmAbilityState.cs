using Patapon.Mixed.GamePlay.RhythmEngine;
using Patapon.Mixed.GamePlay.Units;
using Patapon.Mixed.RhythmEngine;
using Patapon.Mixed.RhythmEngine.Flow;
using Patapon4TLB.Default.Player;
using Revolution;
using Unity.Entities;
using Unity.Networking.Transport;
using UnityEngine;

namespace Patapon.Mixed.GamePlay
{
	/// <summary>
	///     Rhythm based ability.
	/// </summary>
	public struct RhythmAbilityState : IComponentData, IReadWriteComponentSnapshot<RhythmAbilityState, GhostSetup>, ISnapshotDelta<RhythmAbilityState>
	{
		public bool IsActive => IsSelectionActive && IsRhythmActive;

		internal int PreviousActiveStartTime;

		public GameComboState PreviousActiveCombo;
		public GameComboState Combo;

		public AbilitySelection TargetSelection;
		public bool             IsSelectionActive;

		public Entity Engine;
		public Entity Command;
		public bool   IsRhythmActive;
		public bool   CanBeTransitioned;
		public int    ActiveId;
		public bool   IsStillChaining;
		public bool   WillBeActive;
		public int    StartTime;

		public void CalculateWithValidCommand(GameCommandState commandState, GameComboState combo, FlowEngineProcess process, RhythmEngineState state)
		{
			Calculate(new RhythmCurrentCommand {CommandTarget = Command}, commandState, combo, process, state);
		}

		public void Calculate(RhythmCurrentCommand currCommand, GameCommandState commandState, GameComboState combo, FlowEngineProcess process, RhythmEngineState state, bool forceSelectionActive = false)
		{
			if (ActiveId == 0)
				ActiveId++;

			if (combo.Chain != 0)
				PreviousActiveCombo = combo;

			if (currCommand.CommandTarget != Command)
			{
				IsRhythmActive    = IsRhythmActive && commandState.StartTime > process.Milliseconds && currCommand.Previous == Command;
				IsStillChaining   = IsStillChaining && commandState.StartTime > process.Milliseconds && currCommand.Previous == Command;
				StartTime         = -1;
				WillBeActive      = false;
				CanBeTransitioned = true;

				return;
			}

			IsRhythmActive    = commandState.IsGamePlayActive(process.Milliseconds);
			IsSelectionActive = forceSelectionActive || commandState.Selection == TargetSelection;
			// todo: should not be a magic number, retrieve it from settings instead
			CanBeTransitioned = commandState.IsInputActive(process.Milliseconds, 500);

			if (IsActive && PreviousActiveStartTime != commandState.StartTime)
			{
				PreviousActiveStartTime = commandState.StartTime;
				ActiveId++;
			}

			Combo = combo;

			StartTime = commandState.StartTime;
			if (IsSelectionActive)
			{
				IsStillChaining = commandState.StartTime <= process.Milliseconds + (IsStillChaining ? 1000 : 0) && combo.Chain > 0;
				WillBeActive    = commandState.StartTime > process.Milliseconds && process.Milliseconds <= commandState.EndTime && !IsRhythmActive;
			}
			else
			{
				IsStillChaining = false;
				WillBeActive    = false;
			}
		}

		public struct Exclude : IComponentData
		{
		}

		public class NetSynchronize : MixedComponentSnapshotSystemDelta<RhythmAbilityState, GhostSetup>
		{
			public override ComponentType ExcludeComponent => typeof(Exclude);
		}

		public void WriteTo(DataStreamWriter writer, ref RhythmAbilityState baseline, GhostSetup setup, SerializeClientData jobData)
		{
			writer.WritePackedUInt(setup[Command], jobData.NetworkCompressionModel);
		}

		public void ReadFrom(ref DataStreamReader.Context ctx, DataStreamReader reader, ref RhythmAbilityState baseline, DeserializeClientData jobData)
		{
			jobData.GhostToEntityMap.TryGetValue(reader.ReadPackedUInt(ref ctx, jobData.NetworkCompressionModel), out Command);
		}

		public bool DidChange(RhythmAbilityState baseline)
		{
			return Command != baseline.Command;
		}
	}
}