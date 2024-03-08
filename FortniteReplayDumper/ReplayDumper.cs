using FortniteReplayReader;
using FortniteReplayReader.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unreal.Core;
using Unreal.Core.Exceptions;
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
                    ReadReplayData(archive, chunkSize);
                }
                else if (chunkType == ReplayChunkType.Header)
                {
                    //Copy over bytes
                    _writer.Write(chunkSize);
                    _writer.Write(archive.ReadBytes(chunkSize));

                    _writer.Flush();
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
            //8 bytes for the FileMagic and ReplayVersion
            int startPosition = archive.Position + 8;

            base.ReadReplayInfo(archive);

            int endPosition = archive.Position;

            _writer.Write(FileMagic);
            _writer.Write((uint)archive.ReplayVersion);

            if(archive.ReplayVersion >= ReplayVersionHistory.HISTORY_CUSTOM_VERSIONS)
            {
                archive.Seek(startPosition);

                var customVersionLength = archive.ReadUInt32();

                _writer.Write(customVersionLength);

                // version guid -> 16 bytes
                // version -> 4 bytes
                var customVersions = archive.ReadBytes(customVersionLength * 20);

                _writer.Write(customVersions);

                archive.Seek(endPosition);
            }

            //ReplayInfo
            _writer.Write(Replay.Info.LengthInMs);
            _writer.Write(Replay.Info.NetworkVersion);
            _writer.Write(Replay.Info.Changelist);
            _writer.Write(Replay.Info.FriendlyName, 256, true);
            _writer.Write(Convert.ToInt32(Replay.Info.IsLive));

            if(Replay.Info.FileVersion >= ReplayVersionHistory.HISTORY_RECORDED_TIMESTAMP)
            {
                _writer.Write(Replay.Info.Timestamp.ToBinary());
            }

            if (Replay.Info.FileVersion >= ReplayVersionHistory.HISTORY_COMPRESSION)
            {
                _writer.Write(0); //Disable compression
            }

            if (Replay.Info.FileVersion >= ReplayVersionHistory.HISTORY_ENCRYPTION)
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
            using var binaryArchive = (Unreal.Core.BinaryReader)Decompress(decrypted);

            _writer.Write(infoSize + binaryArchive.Bytes.Length); //Chunk Size
            _writer.Write(info.Id);
            _writer.Write(info.Group);
            _writer.Write(info.Metadata);
            _writer.Write(info.StartTime);
            _writer.Write(info.EndTime);
            _writer.Write(binaryArchive.Bytes.Length); //Decompressed checkpoint length
            _writer.Write(binaryArchive.Bytes.ToArray()); //Decompressed checkpoint
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

        public override void ReadReplayData(FArchive archive, int fallbackChunkSize)
        {
            int startPosition = archive.Position;

            var info = new ReplayDataInfo();

            if (archive.ReplayVersion.HasFlag(ReplayVersionHistory.HISTORY_STREAM_CHUNK_TIMES))
            {
                info.Start = archive.ReadUInt32();
                info.End = archive.ReadUInt32();
                info.Length = archive.ReadInt32();
            }
            else
            {
                info.Length = fallbackChunkSize;
            }

            if (archive.ReplayVersion >= ReplayVersionHistory.HISTORY_ENCRYPTION)
            {
                var _ = archive.ReadInt32();
            }

            int infoSize = archive.Position - startPosition;

            using var decryptedReader = DecryptBuffer(archive, info.Length);
            using var binaryArchive = (Unreal.Core.BinaryReader)Decompress(decryptedReader);

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
