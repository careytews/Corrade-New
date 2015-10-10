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
            public static Action<CorradeCommandParameters, Dictionary<string, string>> startproposal =
                (corradeCommandParameters, result) =>
                {
                    if (!HasCorradePermission(corradeCommandParameters.Group.Name, (int) Permissions.Group))
                    {
                        throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                    }
                    IEnumerable<UUID> currentGroups = Enumerable.Empty<UUID>();
                    if (
                        !GetCurrentGroups(corradeConfiguration.ServicesTimeout,
                            ref currentGroups))
                    {
                        throw new ScriptException(ScriptError.COULD_NOT_GET_CURRENT_GROUPS);
                    }
                    if (!new HashSet<UUID>(currentGroups).Contains(corradeCommandParameters.Group.UUID))
                    {
                        throw new ScriptException(ScriptError.NOT_IN_GROUP);
                    }
                    if (
                        !HasGroupPowers(Client.Self.AgentID, corradeCommandParameters.Group.UUID,
                            GroupPowers.StartProposal,
                            corradeConfiguration.ServicesTimeout, corradeConfiguration.DataTimeout))
                    {
                        throw new ScriptException(ScriptError.NO_GROUP_POWER_FOR_COMMAND);
                    }
                    int duration;
                    if (
                        !int.TryParse(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.DURATION)),
                                    corradeCommandParameters.Message)),
                            out duration))
                    {
                        throw new ScriptException(ScriptError.INVALID_PROPOSAL_DURATION);
                    }
                    float majority;
                    if (
                        !float.TryParse(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.MAJORITY)),
                                    corradeCommandParameters.Message)),
                            out majority))
                    {
                        throw new ScriptException(ScriptError.INVALID_PROPOSAL_MAJORITY);
                    }
                    int quorum;
                    if (
                        !int.TryParse(
                            wasInput(
                                wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.QUORUM)),
                                    corradeCommandParameters.Message)),
                            out quorum))
                    {
                        throw new ScriptException(ScriptError.INVALID_PROPOSAL_QUORUM);
                    }
                    string text =
                        wasInput(wasKeyValueGet(wasOutput(wasGetDescriptionFromEnumValue(ScriptKeys.TEXT)),
                            corradeCommandParameters.Message));
                    if (string.IsNullOrEmpty(text))
                    {
                        throw new ScriptException(ScriptError.INVALID_PROPOSAL_TEXT);
                    }
                    Client.Groups.StartProposal(corradeCommandParameters.Group.UUID, new GroupProposal
                    {
                        Duration = duration,
                        Majority = majority,
                        Quorum = quorum,
                        VoteText = text
                    });
                };
        }
    }
}