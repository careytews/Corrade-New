///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using OpenMetaverse;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static Action<CorradeCommandParameters, Dictionary<string, string>> getscriptrunning =
                (corradeCommandParameters, result) =>
                {
                    if (!HasCorradePermission(corradeCommandParameters.Group.Name, (int) Permissions.Interact))
                    {
                        throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                    }
                    float range;
                    if (
                        !float.TryParse(
                            wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.RANGE)),
                                corradeCommandParameters.Message)),
                            out range))
                    {
                        range = corradeConfiguration.Range;
                    }
                    string entity =
                        wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ENTITY)),
                            corradeCommandParameters.Message));
                    UUID entityUUID;
                    if (!UUID.TryParse(entity, out entityUUID))
                    {
                        if (string.IsNullOrEmpty(entity))
                        {
                            throw new ScriptException(ScriptError.UNKNOWN_ENTITY);
                        }
                        entityUUID = UUID.Zero;
                    }
                    Primitive primitive = null;
                    if (
                        !FindPrimitive(
                            StringOrUUID(wasInput(wasKeyValueGet(
                                wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.ITEM)),
                                corradeCommandParameters.Message))),
                            range,
                            ref primitive, corradeConfiguration.ServicesTimeout, corradeConfiguration.DataTimeout))
                    {
                        throw new ScriptException(ScriptError.PRIMITIVE_NOT_FOUND);
                    }
                    List<InventoryBase> inventory =
                        Client.Inventory.GetTaskInventory(primitive.ID, primitive.LocalID,
                            (int) corradeConfiguration.ServicesTimeout).ToList();
                    InventoryItem item = !entityUUID.Equals(UUID.Zero)
                        ? inventory.AsParallel().FirstOrDefault(o => o.UUID.Equals(entityUUID)) as InventoryItem
                        : inventory.AsParallel().FirstOrDefault(o => o.Name.Equals(entity)) as InventoryItem;
                    if (item == null)
                    {
                        throw new ScriptException(ScriptError.INVENTORY_ITEM_NOT_FOUND);
                    }
                    switch (item.AssetType)
                    {
                        case AssetType.LSLBytecode:
                        case AssetType.LSLText:
                            break;
                        default:
                            throw new ScriptException(ScriptError.ITEM_IS_NOT_A_SCRIPT);
                    }
                    ManualResetEvent ScriptRunningReplyEvent = new ManualResetEvent(false);
                    bool running = false;
                    EventHandler<ScriptRunningReplyEventArgs> ScriptRunningEventHandler = (sender, args) =>
                    {
                        running = args.IsRunning;
                        ScriptRunningReplyEvent.Set();
                    };
                    lock (ClientInstanceInventoryLock)
                    {
                        Client.Inventory.ScriptRunningReply += ScriptRunningEventHandler;
                        Client.Inventory.RequestGetScriptRunning(primitive.ID, item.UUID);
                        if (!ScriptRunningReplyEvent.WaitOne((int) corradeConfiguration.ServicesTimeout, false))
                        {
                            Client.Inventory.ScriptRunningReply -= ScriptRunningEventHandler;
                            throw new ScriptException(ScriptError.TIMEOUT_GETTING_SCRIPT_STATE);
                        }
                        Client.Inventory.ScriptRunningReply -= ScriptRunningEventHandler;
                    }
                    result.Add(wasGetDescriptionFromEnumValue(ResultKeys.DATA), running.ToString());
                };
        }
    }
}