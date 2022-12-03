﻿using LabFusion.Data;
using LabFusion.Utilities;
using LabFusion.Syncables;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using LabFusion.Extensions;

namespace LabFusion.Network
{
    public class PropSyncableUpdateData : IFusionSerializable, IDisposable
    {
        public byte ownerId;
        public ushort syncId;
        public bool isRotationBased;
        public byte length;
        public Vector3[] serializedPositions;
        public SerializedQuaternion[] serializedQuaternions;
        public float velocity;

        public void Serialize(FusionWriter writer)
        {
            writer.Write(ownerId);
            writer.Write(syncId);

            writer.Write(isRotationBased);
            writer.Write(length);

            for (var i = 0; i < serializedPositions.Length; i++) {
                if (i > 0 && isRotationBased)
                    break;

                var position = serializedPositions[i];
                writer.Write(position);
            }

            foreach (var rotation in serializedQuaternions)
                writer.Write(rotation);

            writer.Write(velocity);
        }

        public void Deserialize(FusionReader reader)
        {
            ownerId = reader.ReadByte();
            syncId = reader.ReadUInt16();
            isRotationBased = reader.ReadBoolean();
            length = reader.ReadByte();

            serializedPositions = new Vector3[length];
            serializedQuaternions = new SerializedQuaternion[length];

            for (var i = 0; i < length; i++) {
                if (i > 0 && isRotationBased)
                    break;

                serializedPositions[i] = reader.ReadVector3();
            }

            for (var i = 0; i < length; i++) {
                serializedQuaternions[i] = reader.ReadFusionSerializable<SerializedQuaternion>();
            }

            velocity = reader.ReadSingle();
        }

        public PropSyncable GetPropSyncable() {
            if (SyncManager.TryGetSyncable(syncId, out var syncable) && syncable is PropSyncable propSyncable)
                return propSyncable;

            return null;
        }

        public void Dispose() {
            GC.SuppressFinalize(this);
        }

        public static PropSyncableUpdateData Create(byte ownerId, PropSyncable syncable)
        {
            var syncId = syncable.GetId();
            var hosts = syncable.HostGameObjects;
            var rigidbodies = syncable.Rigidbodies;

            int length = rigidbodies.Length;

            var data = new PropSyncableUpdateData {
                ownerId = ownerId,
                syncId = syncId,
                isRotationBased = syncable.IsRotationBased,
                length = (byte)length,
                serializedPositions = new Vector3[length],
                serializedQuaternions = new SerializedQuaternion[length],
                velocity = 0f,
            };

            float maxVelocity = 0f;

            for (var i = 0; i < rigidbodies.Length; i++) {
                var rb = rigidbodies[i];
                var host = hosts[i];

                if (!host.IsNOC()) {
                    data.serializedPositions[i] = host.transform.position;
                    data.serializedQuaternions[i] = SerializedQuaternion.Compress(host.transform.rotation);
                }
                else {
                    data.serializedPositions[i] = Vector3.zero;
                    data.serializedQuaternions[i] = SerializedQuaternion.Compress(Quaternion.identity);
                }

                if (!rb.IsNOC()) {
                    maxVelocity = Mathf.Max(maxVelocity, rb.velocity.sqrMagnitude);
                }
            }

            return data;
        }
    }

    [Net.SkipHandleWhileLoading]
    public class PropSyncableUpdateMessage : FusionMessageHandler
    {
        public override byte? Tag => NativeMessageTag.PropSyncableUpdate;

        public override void HandleMessage(byte[] bytes, bool isServerHandled = false)
        {
            using (var reader = FusionReader.Create(bytes)) {
                using (var data = reader.ReadFusionSerializable<PropSyncableUpdateData>()) {
                    // Find the prop syncable and update its info
                    var syncable = data.GetPropSyncable();
                    if (syncable != null && syncable.IsRegistered() && syncable.Owner.HasValue && syncable.Owner.Value == data.ownerId) {
                        syncable.TimeOfMessage = Time.timeSinceLevelLoad;
                        
                        for (var i = 0; i < data.length; i++) {
                            syncable.DesiredPositions[i] = data.serializedPositions[i];
                            syncable.DesiredRotations[i] = data.serializedQuaternions[i].Expand();
                            syncable.DesiredVelocity = data.velocity;

                            var rb = syncable.Rigidbodies[i];

                            if (!rb.IsNOC() && rb.IsSleeping())
                                rb.WakeUp();
                        }
                    }

                    // Send message to other clients if server
                    if (NetworkInfo.IsServer && isServerHandled) {
                        using (var message = FusionMessage.Create(Tag.Value, bytes)) {
                            MessageSender.BroadcastMessageExcept(data.ownerId, NetworkChannel.Unreliable, message);
                        }
                    }
                }
            }
        }
    }
}