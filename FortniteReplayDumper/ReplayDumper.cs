﻿using FortniteReplayReader;
using FortniteReplayReader.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unreal.Core;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace FortniteReplayDumper
{
    public class ReplayDumper : ReplayReader
    {
        private FileStream _outputStream;
        private UnrealBinaryWriter _writer;

        public ReplayDumper(ILogger logger = null) : base(logger)
        {
        }

        public void DumpReplay(string replayFile, string destinationFile)
        {
            _outputStream = new FileStream(destinationFile, FileMode.Create);
            _writer = new UnrealBinaryWriter(_outputStream);
            _writer.Flush();

            bool delete = false;

            try
            {
                ReadReplay(replayFile);
            }
            catch
            {
                delete = true;

                throw;
            }
            finally
            {
                _outputStream?.Dispose();
                _writer.Dispose();

                if (delete)
                {
                    File.Delete(destinationFile);
                }
            }
        }

        public override void ReadReplayChunks(FArchive archive)
        {
            while (!archive.AtEnd())
            {
                var chunkType = archive.ReadUInt32AsEnum<ReplayChunkType>();
                _writer.Write((uint)chunkType);

                var chunkSize = archive.ReadInt32();
                var offset = archive.Position;

                //Console.WriteLine($"Chunk {chunkType}. Size: {chunkSize}. Offset: {offset}");

                if (chunkType == ReplayChunkType.Checkpoint)
                {
                    ReadCheckpoint(archive);
                }
                else if (chunkType == ReplayChunkType.Event)
                {
                    ReadEvent(archive);
                }
                else if (chunkType == ReplayChunkType.ReplayData)
                {
                    ReadReplayData(archive);
                }
                else if (chunkType == ReplayChunkType.Header)
                {
                    ReadReplayHeader(archive);
                }

                if (archive.Position != offset + chunkSize)
                {
                    //_logger?.LogWarning($"Chunk ({chunkType}) at offset {offset} not correctly read...");
                    archive.Seek(offset + chunkSize, SeekOrigin.Begin);
                }
            }
        }

        public override void ReadReplayInfo(FArchive archive)
        {
            base.ReadReplayInfo(archive);

            _writer.Write(FileMagic);
            _writer.Write((uint)archive.ReplayVersion);

            //ReplayInfo
            _writer.Write(Replay.Info.LengthInMs);
            _writer.Write(Replay.Info.NetworkVersion);
            _writer.Write(Replay.Info.Changelist);
            _writer.Write(Replay.Info.FriendlyName, 256, true);
            _writer.Write(Convert.ToInt32(Replay.Info.IsLive));

            if(Replay.Info.FileVersion >= Unreal.Core.Models.ReplayVersionHistory.HISTORY_RECORDED_TIMESTAMP)
            {
                _writer.Write(Replay.Info.Timestamp.ToBinary());
            }

            if (Replay.Info.FileVersion >= Unreal.Core.Models.ReplayVersionHistory.HISTORY_COMPRESSION)
            {
                _writer.Write(0); //Disable compression
            }

            if (Replay.Info.FileVersion >= Unreal.Core.Models.ReplayVersionHistory.HISTORY_ENCRYPTION)
            {
                _writer.Write(0); //Disable encryption
                _writer.WriteArray(new byte[32], _writer.Write); //Write empty key
            }
        }

        public override void ReadCheckpoint(FArchive archive)
        {
            int startPosition = archive.Position;

            var info = new CheckpointInfo
            {
                Id = archive.ReadFString(),
                Group = archive.ReadFString(),
                Metadata = archive.ReadFString(),
                StartTime = archive.ReadUInt32(),
                EndTime = archive.ReadUInt32(),
                SizeInBytes = archive.ReadInt32()
            };

            int infoSize = archive.Position - startPosition;

            using var decrypted = (Unreal.Core.BinaryReader)DecryptBuffer(archive, info.SizeInBytes);
            using var binaryArchive = (Unreal.Core.BinaryReader)Decompress(decrypted, decrypted.Bytes.Length);

            _writer.Write(infoSize + binaryArchive.Bytes.Length); //Chunk Size
            _writer.Write(info.Id);
            _writer.Write(info.Group);
            _writer.Write(info.Metadata);
            _writer.Write(info.StartTime);
            _writer.Write(info.EndTime);
            _writer.Write(binaryArchive.Bytes.Length); //Decompressed checkpoint length
            _writer.Write(binaryArchive.Bytes.ToArray()); //Decompressed checkpoint
        }

        public override void ReadReplayHeader(FArchive archive)
        {
            //Nothing needs to be changed here, so we can copy over the bytes
            int oldPosition = archive.Position;

            base.ReadReplayHeader(archive);

            int newPosition = archive.Position;

            archive.Seek(oldPosition);

            int bytesWritten = newPosition - oldPosition;

            _writer.Write(bytesWritten);
            _writer.Write(archive.ReadBytes(bytesWritten));

            _writer.Flush();
        }

        public override void ReadEvent(FArchive archive)
        {
            int startPosition = archive.Position;

            var info = new EventInfo
            {
                Id = archive.ReadFString(),
                Group = archive.ReadFString(),
                Metadata = archive.ReadFString(),
                StartTime = archive.ReadUInt32(),
                EndTime = archive.ReadUInt32(),
                SizeInBytes = archive.ReadInt32()
            };

            int infoSize = archive.Position - startPosition;

            using var decryptedReader = (Unreal.Core.BinaryReader)base.DecryptBuffer(archive, info.SizeInBytes);

            _writer.Write(infoSize + decryptedReader.Bytes.Length); //Chunk Size
            _writer.Write(info.Id);
            _writer.Write(info.Group);
            _writer.Write(info.Metadata);
            _writer.Write(info.StartTime);
            _writer.Write(info.EndTime);
            _writer.Write(decryptedReader.Bytes.Length); //Decrypted size
            _writer.Write(decryptedReader.Bytes.ToArray()); //Decrypted bytes
        }

        public override void ReadReplayData(FArchive archive)
        {
            int startPosition = archive.Position;

            var info = new ReplayDataInfo();

            if (archive.ReplayVersion >= ReplayVersionHistory.HISTORY_STREAM_CHUNK_TIMES)
            {
                info.Start = archive.ReadUInt32();
                info.End = archive.ReadUInt32();
                info.Length = archive.ReadUInt32();
            }
            else
            {
                info.Length = archive.ReadUInt32();
            }

            int memorySizeInBytes = (int)info.Length;

            if (archive.ReplayVersion >= ReplayVersionHistory.HISTORY_ENCRYPTION)
            {
                memorySizeInBytes = archive.ReadInt32();
            }

            int infoSize = archive.Position - startPosition;

            using var decryptedReader = DecryptBuffer(archive, (int)info.Length);
            using var binaryArchive = (Unreal.Core.BinaryReader)Decompress(decryptedReader, memorySizeInBytes);

            //Chunk size
            _writer.Write(infoSize + binaryArchive.Bytes.Length);

            if (archive.ReplayVersion >= ReplayVersionHistory.HISTORY_STREAM_CHUNK_TIMES)
            {
                _writer.Write(info.Start);
                _writer.Write(info.End);
                _writer.Write(binaryArchive.Bytes.Length);
            }
            else
            {
                _writer.Write(binaryArchive.Bytes.Length);
            }

            if (archive.ReplayVersion >= ReplayVersionHistory.HISTORY_ENCRYPTION)
            {
                _writer.Write(binaryArchive.Bytes.Length);
            }

            _writer.Write(binaryArchive.Bytes.ToArray());
        }
    }
}
