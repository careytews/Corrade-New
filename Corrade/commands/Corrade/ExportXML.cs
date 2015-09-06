///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using OpenMetaverse;
using OpenMetaverse.Assets;
using OpenMetaverse.Imaging;
using OpenMetaverse.StructuredData;
using Encoder = System.Drawing.Imaging.Encoder;
using Parallel = System.Threading.Tasks.Parallel;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static Action<Group, string, Dictionary<string, string>> exportxml =
                (commandGroup, message, result) =>
                {
                    if (
                        !HasCorradePermission(commandGroup.Name,
                            (int) Permissions.Interact))
                    {
                        throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                    }
                    float range;
                    if (
                        !float.TryParse(
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)), message)),
                            out range))
                    {
                        range = corradeConfiguration.Range;
                    }
                    Primitive primitive = null;
                    if (
                        !FindPrimitive(
                            StringOrUUID(wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)), message))),
                            range,
                            ref primitive, corradeConfiguration.ServicesTimeout, corradeConfiguration.DataTimeout))
                    {
                        throw new ScriptException(ScriptError.PRIMITIVE_NOT_FOUND);
                    }

                    // if the primitive is not an object (the root) or the primitive
                    // is not an object as an avatar attachment then do not export it.
                    if (!primitive.ParentID.Equals(0) && !GetAvatars(range, corradeConfiguration.ServicesTimeout,
                        corradeConfiguration.DataTimeout)
                        .AsParallel()
                        .Any(o => o.LocalID.Equals(primitive.ParentID)))
                    {
                        throw new ScriptException(ScriptError.ITEM_IS_NOT_AN_OBJECT);
                    }

                    HashSet<Primitive> exportPrimitivesSet = new HashSet<Primitive>();
                    Primitive root = new Primitive(primitive) {Position = Vector3.Zero};
                    exportPrimitivesSet.Add(root);

                    object LockObject = new object();

                    // find all the children that have the object as parent.
                    Parallel.ForEach(GetPrimitives(range, corradeConfiguration.ServicesTimeout,
                        corradeConfiguration.DataTimeout), o =>
                        {
                            if (!o.ParentID.Equals(root.LocalID))
                                return;
                            Primitive child = new Primitive(o);
                            child.Position = root.Position + child.Position*root.Rotation;
                            child.Rotation = root.Rotation*child.Rotation;
                            lock (LockObject)
                            {
                                exportPrimitivesSet.Add(child);
                            }
                        });

                    // add all the textures to export
                    HashSet<UUID> exportTexturesSet = new HashSet<UUID>();
                    Parallel.ForEach(exportPrimitivesSet, o =>
                    {
                        if (!o.Textures.DefaultTexture.TextureID.Equals(Primitive.TextureEntry.WHITE_TEXTURE) &&
                            !exportTexturesSet.Contains(o.Textures.DefaultTexture.TextureID))
                        {
                            lock (LockObject)
                            {
                                exportTexturesSet.Add(new UUID(o.Textures.DefaultTexture.TextureID));
                            }
                        }
                        Parallel.ForEach(o.Textures.FaceTextures, p =>
                        {
                            if (p != null &&
                                !p.TextureID.Equals(Primitive.TextureEntry.WHITE_TEXTURE) &&
                                !exportTexturesSet.Contains(p.TextureID))
                            {
                                lock (LockObject)
                                {
                                    exportTexturesSet.Add(new UUID(p.TextureID));
                                }
                            }
                        });
                        if (o.Sculpt != null && !o.Sculpt.SculptTexture.Equals(UUID.Zero) &&
                            !exportTexturesSet.Contains(o.Sculpt.SculptTexture))
                        {
                            lock (LockObject)
                            {
                                exportTexturesSet.Add(new UUID(o.Sculpt.SculptTexture));
                            }
                        }
                    });

                    // Get the destination format to convert the downloaded textures to.
                    string format =
                        wasInput(wasKeyValueGet(
                            wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FORMAT)),
                            message));
                    PropertyInfo formatProperty = null;
                    if (!string.IsNullOrEmpty(format))
                    {
                        formatProperty = typeof (ImageFormat).GetProperties(
                            BindingFlags.Public |
                            BindingFlags.Static)
                            .AsParallel().FirstOrDefault(
                                o =>
                                    format.Equals(o.Name, StringComparison.Ordinal));
                        if (formatProperty == null)
                        {
                            throw new ScriptException(ScriptError.UNKNOWN_IMAGE_FORMAT_REQUESTED);
                        }
                    }

                    // download all the textures.
                    Dictionary<string, byte[]> exportTextureSetFiles = new Dictionary<string, byte[]>();
                    Parallel.ForEach(exportTexturesSet, o =>
                    {
                        byte[] assetData = null;
                        switch (!Client.Assets.Cache.HasAsset(o))
                        {
                            case true:
                                lock (ClientInstanceAssetsLock)
                                {
                                    ManualResetEvent RequestAssetEvent = new ManualResetEvent(false);
                                    Client.Assets.RequestImage(o, ImageType.Normal,
                                        delegate(TextureRequestState state, AssetTexture asset)
                                        {
                                            if (!asset.AssetID.Equals(o)) return;
                                            if (!state.Equals(TextureRequestState.Finished)) return;
                                            assetData = asset.AssetData;
                                            RequestAssetEvent.Set();
                                        });
                                    if (
                                        !RequestAssetEvent.WaitOne((int) corradeConfiguration.ServicesTimeout, false))
                                    {
                                        throw new ScriptException(ScriptError.TIMEOUT_TRANSFERRING_ASSET);
                                    }
                                }
                                Client.Assets.Cache.SaveAssetToCache(o, assetData);
                                break;
                            default:
                                assetData = Client.Assets.Cache.GetCachedAssetBytes(o);
                                break;
                        }
                        switch (formatProperty != null)
                        {
                            case true:
                                ManagedImage managedImage;
                                if (!OpenJPEG.DecodeToImage(assetData, out managedImage))
                                {
                                    throw new Exception(
                                        wasGetDescriptionFromEnumValue(
                                            ScriptError.UNABLE_TO_DECODE_ASSET_DATA));
                                }
                                using (MemoryStream imageStream = new MemoryStream())
                                {
                                    try
                                    {
                                        using (Bitmap bitmapImage = managedImage.ExportBitmap())
                                        {
                                            EncoderParameters encoderParameters =
                                                new EncoderParameters(1);
                                            encoderParameters.Param[0] =
                                                new EncoderParameter(Encoder.Quality, 100L);
                                            bitmapImage.Save(imageStream,
                                                ImageCodecInfo.GetImageDecoders()
                                                    .AsParallel()
                                                    .FirstOrDefault(
                                                        p =>
                                                            p.FormatID.Equals(
                                                                ((ImageFormat)
                                                                    formatProperty.GetValue(
                                                                        new ImageFormat(Guid.Empty)))
                                                                    .Guid)),
                                                encoderParameters);
                                        }
                                    }
                                    catch (Exception)
                                    {
                                        throw new Exception(
                                            wasGetDescriptionFromEnumValue(
                                                ScriptError.UNABLE_TO_CONVERT_TO_REQUESTED_FORMAT));
                                    }
                                    lock (LockObject)
                                    {
                                        exportTextureSetFiles.Add(
                                            o + "." + format.ToLower(),
                                            imageStream.ToArray());
                                    }
                                }
                                break;
                            default:
                                format = "j2c";
                                lock (LockObject)
                                {
                                    exportTextureSetFiles.Add(o + "." + "j2c",
                                        assetData);
                                }
                                break;
                        }
                    });

                    HashSet<char> invalidPathCharacters = new HashSet<char>(Path.GetInvalidPathChars());

                    using (MemoryStream zipMemoryStream = new MemoryStream())
                    {
                        using (
                            ZipArchive zipOutputStream = new ZipArchive(zipMemoryStream, ZipArchiveMode.Create, true)
                            )
                        {
                            ZipArchive zipOutputStreamClosure = zipOutputStream;
                            // add all the textures to the zip file
                            Parallel.ForEach(exportTextureSetFiles, o =>
                            {
                                lock (LockObject)
                                {
                                    ZipArchiveEntry textureEntry =
                                        zipOutputStreamClosure.CreateEntry(
                                            new string(
                                                o.Key.Where(p => !invalidPathCharacters.Contains(p)).ToArray()));
                                    using (Stream textureEntryDataStream = textureEntry.Open())
                                    {
                                        using (
                                            BinaryWriter textureEntryDataStreamWriter =
                                                new BinaryWriter(textureEntryDataStream, Encoding.UTF8))
                                        {
                                            textureEntryDataStreamWriter.Write(o.Value);
                                            textureEntryDataStream.Flush();
                                        }
                                    }
                                }
                            });

                            // add the primitives XML data to the zip file
                            ZipArchiveEntry primitiveEntry =
                                zipOutputStreamClosure.CreateEntry(
                                    new string(
                                        (primitive.Properties.Name + ".xml").Where(
                                            p => !invalidPathCharacters.Contains(p))
                                            .ToArray()));
                            using (Stream primitiveEntryDataStream = primitiveEntry.Open())
                            {
                                using (
                                    StreamWriter primitiveEntryDataStreamWriter =
                                        new StreamWriter(primitiveEntryDataStream, Encoding.UTF8))
                                {
                                    primitiveEntryDataStreamWriter.Write(
                                        OSDParser.SerializeLLSDXmlString(
                                            Helpers.PrimListToOSD(exportPrimitivesSet.ToList())));
                                    primitiveEntryDataStreamWriter.Flush();
                                }
                            }
                        }

                        // Base64-encode the zip stream and send it.
                        zipMemoryStream.Seek(0, SeekOrigin.Begin);

                        // If no path was specificed, then send the data.
                        string path =
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.PATH)),
                                message));
                        if (string.IsNullOrEmpty(path))
                        {
                            result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA),
                                Convert.ToBase64String(zipMemoryStream.ToArray()));
                            return;
                        }
                        if (
                            !HasCorradePermission(commandGroup.Name, (int) Permissions.System))
                        {
                            throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                        }
                        // Otherwise, save it to the specified file.
                        using (StreamWriter sw = new StreamWriter(path, false, Encoding.UTF8))
                        {
                            zipMemoryStream.WriteTo(sw.BaseStream);
                            zipMemoryStream.Flush();
                        }
                    }
                };
        }
    }
}