///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2013 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using OpenMetaverse;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static Action<Group, string, Dictionary<string, string>> primitivebuy =
                (commandGroup, message, result) =>
                {
                    if (!HasCorradePermission(commandGroup.Name, (int) Permissions.Interact))
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
                    if (primitive.Properties.SaleType.Equals(SaleType.Not))
                    {
                        throw new ScriptException(ScriptError.PRIMITIVE_NOT_FOR_SALE);
                    }
                    if (!primitive.Properties.SalePrice.Equals(0) &&
                        !HasCorradePermission(commandGroup.Name, (int) Permissions.Economy))
                    {
                        throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                    }
                    UUID folderUUID;
                    string folder =
                        wasInput(
                            wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.FOLDER)),
                                message));
                    if (string.IsNullOrEmpty(folder) || !UUID.TryParse(folder, out folderUUID))
                    {
                        folderUUID = Client.Inventory.Store.RootFolder.UUID;
                    }
                    Client.Objects.BuyObject(
                        Client.Network.Simulators.FirstOrDefault(o => o.Handle.Equals(primitive.RegionHandle)),
                        primitive.LocalID, primitive.Properties.SaleType,
                        primitive.Properties.SalePrice,
                        commandGroup.UUID, folderUUID);
                };
        }
    }
}