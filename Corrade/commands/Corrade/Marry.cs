﻿///////////////////////////////////////////////////////////////////////////
//  Copyright (C) Wizardry and Steamworks 2016 - License: GNU GPLv3      //
//  Please see: http://www.gnu.org/licenses/gpl.html for legal details,  //
//  rights of fair usage, the disclaimer and warranty conditions.        //
///////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using CorradeConfiguration;
using HtmlAgilityPack;
using wasOpenMetaverse;
using wasSharp;

namespace Corrade
{
    public partial class Corrade
    {
        public partial class CorradeCommands
        {
            public static Action<CorradeCommandParameters, Dictionary<string, string>> marry =
                (corradeCommandParameters, result) =>
                {
                    if (
                        !HasCorradePermission(corradeCommandParameters.Group.UUID,
                            (int) Configuration.Permissions.Interact))
                    {
                        throw new ScriptException(ScriptError.NO_CORRADE_PERMISSIONS);
                    }

                    var firstname = wasInput(
                        KeyValue.Get(
                            wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.FIRSTNAME)),
                            corradeCommandParameters.Message));

                    var lastname = wasInput(
                        KeyValue.Get(
                            wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.LASTNAME)),
                            corradeCommandParameters.Message));

                    if (string.IsNullOrEmpty(firstname) && string.IsNullOrEmpty(lastname))
                    {
                        firstname = Client.Self.FirstName;
                        lastname = Client.Self.LastName;
                    }

                    var secret = wasInput(
                        KeyValue.Get(
                            wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.SECRET)),
                            corradeCommandParameters.Message));
                    if (string.IsNullOrEmpty(secret))
                        throw new ScriptException(ScriptError.NO_SECRET_PROVIDED);

                    var cookieContainer = new CookieContainer();

                    #region Authenticate

                    var postData = Web.wasPOST(CORRADE_CONSTANTS.USER_AGENT,
                        "https://id.secondlife.com/openid/loginsubmit",
                        new Dictionary<string, string>
                        {
                            {"username", $"{firstname} {lastname}"},
                            {"password", secret},
                            {"language", "en_US"},
                            {"previous_language", "en_US"},
                            {"from_amazon", "False"},
                            {"stay_logged_in", "True"},
                            {"show_join", "False"},
                            {"return_to", "https://secondlife.com/auth/oid_return.php"}
                        }, CorradePOSTMediaType, cookieContainer, corradeConfiguration.ServicesTimeout);

                    if (postData.Result == null)
                        throw new ScriptException(ScriptError.UNABLE_TO_AUTHENTICATE);

                    var doc = new HtmlDocument();
                    HtmlNode.ElementsFlags.Remove("form");
                    doc.LoadHtml(Encoding.UTF8.GetString(postData.Result));

                    var openIDNodes = doc.DocumentNode.SelectNodes("//form[@id='openid_message']/input[@type='hidden']");
                    if (openIDNodes == null || !openIDNodes.Any())
                        throw new ScriptException(ScriptError.UNABLE_TO_AUTHENTICATE);

                    var openID =
                        openIDNodes.AsParallel()
                            .Where(
                                o =>
                                    o.Attributes.Contains("name") && o.Attributes["name"].Value != null &&
                                    o.Attributes.Contains("value") && o.Attributes["value"].Value != null)
                            .ToDictionary(o => o.Attributes["name"].Value,
                                o => o.Attributes["value"].Value);

                    if (!openID.Any())
                        throw new ScriptException(ScriptError.UNABLE_TO_AUTHENTICATE);

                    postData = Web.wasPOST(CORRADE_CONSTANTS.USER_AGENT, "https://id.secondlife.com/openid/openidserver",
                        openID, CorradePOSTMediaType,
                        cookieContainer,
                        corradeConfiguration.ServicesTimeout);

                    if (postData.Result == null)
                        throw new ScriptException(ScriptError.UNABLE_TO_AUTHENTICATE);

                    #endregion

                    HtmlNode revokeNode;
                    HtmlNode formNode;
                    HtmlNode tokenNode;
                    HtmlNode partnerNode;
                    HtmlNodeCollection errorNodes;
                    string message;
                    switch (Reflection.GetEnumValueFromName<Action>(
                        wasInput(
                            KeyValue.Get(wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.ACTION)),
                                corradeCommandParameters.Message))
                            .ToLowerInvariant()))
                    {
                        case Action.PROPOSE: // Send a proposal

                            #region Partnership Parameters

                            var name = wasInput(KeyValue.Get(
                                wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.NAME)),
                                corradeCommandParameters.Message));
                            if (string.IsNullOrEmpty(name))
                                throw new ScriptException(ScriptError.NO_NAME_PROVIDED);

                            message = wasInput(KeyValue.Get(
                                wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.MESSAGE)),
                                corradeCommandParameters.Message));
                            if (string.IsNullOrEmpty(message))
                                throw new ScriptException(ScriptError.NO_MESSAGE_PROVIDED);
                            // Sanitize input.
                            if (Helpers.IsSecondLife(Client))
                            {
                                // Check for description length.
                                if (message.Length > Constants.PARTNERSHIP.MAXIMUM_PROPOSAL_MESSAGE_LENGTH)
                                    throw new ScriptException(ScriptError.TOO_MANY_CHARACTERS_FOR_PROPOSAL_MESSAGE);
                                // Check for description HTML.
                                var descriptionInput = new HtmlDocument();
                                descriptionInput.LoadHtml(message);
                                if (!descriptionInput.DocumentNode.InnerText.Equals(message))
                                    throw new ScriptException(ScriptError.MESSAGE_MAY_NOT_CONTAIN_HTML);
                            }

                            #endregion

                            // Check whether a proposal has been sent.
                            postData = Web.wasGET(CORRADE_CONSTANTS.USER_AGENT,
                                "https://secondlife.com/my/account/partners.php",
                                new Dictionary<string, string>
                                {
                                    {"lang", "en-US"}
                                }, cookieContainer, corradeConfiguration.ServicesTimeout);

                            if (postData.Result == null)
                                throw new ScriptException(ScriptError.UNABLE_TO_REACH_PARTNERSHIP_PAGE);

                            doc = new HtmlDocument();
                            HtmlNode.ElementsFlags.Remove("form");
                            HtmlNode.ElementsFlags.Remove("option");
                            doc.LoadHtml(Encoding.UTF8.GetString(postData.Result));
                            revokeNode =
                                doc.DocumentNode.Descendants("a")
                                    .FirstOrDefault(o => o.Attributes["href"].Value.Equals("?revoke=true"));

                            if (revokeNode != null)
                                throw new ScriptException(ScriptError.PROPOSAL_ALREADY_SENT);

                            // Now send the proposal.
                            formNode = doc.DocumentNode.SelectSingleNode("//form[@class='wht-grybrdr-content']");

                            // Build the new partnership request form.
                            var newProposal = new Dictionary<string, string>();

                            // Get the token.
                            tokenNode = formNode.SelectSingleNode("//input[@name='CSRFToken']");
                            newProposal.Add(tokenNode.Attributes["name"].Value, tokenNode.Attributes["value"].Value);
                            newProposal.Add("send", "Send Proposal");

                            // add username
                            newProposal.Add("form[loginid]", name);
                            // add proposal text <= 254
                            newProposal.Add("form[proposal]", message);

                            // Send the form.
                            postData = Web.wasPOST(CORRADE_CONSTANTS.USER_AGENT,
                                "https://secondlife.com/my/account/partners.php", newProposal,
                                CorradePOSTMediaType,
                                cookieContainer,
                                corradeConfiguration.ServicesTimeout);

                            if (postData.Result == null)
                                throw new ScriptException(ScriptError.UNABLE_TO_POST_PROPOSAL);

                            // Check for proposal errors (ie user already has a partner request, etc...).
                            doc = new HtmlDocument();
                            HtmlNode.ElementsFlags.Remove("form");
                            HtmlNode.ElementsFlags.Remove("option");
                            doc.LoadHtml(Encoding.UTF8.GetString(postData.Result));
                            errorNodes = doc.DocumentNode.SelectNodes("//div[@class='error']/ul/li");
                            if (errorNodes != null && errorNodes.Any())
                            {
                                result.Add(Reflection.GetNameFromEnumValue(ResultKeys.DATA),
                                    CSV.FromEnumerable(errorNodes.Select(o => o.InnerText.Trim())));
                                throw new ScriptException(ScriptError.PROPOSAL_REJECTED);
                            }
                            break;
                        case Action.REVOKE: // Revoke a sent proposal.
                            // Check whether a proposal has been sent.
                            postData = Web.wasGET(CORRADE_CONSTANTS.USER_AGENT,
                                "https://secondlife.com/my/account/partners.php",
                                new Dictionary<string, string>
                                {
                                    {"lang", "en-US"}
                                }, cookieContainer, corradeConfiguration.ServicesTimeout);

                            if (postData.Result == null)
                                throw new ScriptException(ScriptError.UNABLE_TO_REACH_PARTNERSHIP_PAGE);

                            doc = new HtmlDocument();
                            HtmlNode.ElementsFlags.Remove("form");
                            HtmlNode.ElementsFlags.Remove("option");
                            doc.LoadHtml(Encoding.UTF8.GetString(postData.Result));
                            revokeNode =
                                doc.DocumentNode.Descendants("a")
                                    .FirstOrDefault(o => o.Attributes["href"].Value.Equals("?revoke=true"));
                            if (revokeNode == null)
                                throw new ScriptException(ScriptError.NO_PROPOSAL_TO_REJECT);

                            postData = Web.wasGET(CORRADE_CONSTANTS.USER_AGENT,
                                "https://secondlife.com/my/account/partners.php",
                                new Dictionary<string, string>
                                {
                                    {"revoke", "true"},
                                    {"lang", "en-US"}
                                }, cookieContainer, corradeConfiguration.ServicesTimeout);

                            if (postData.Result == null)
                                throw new ScriptException(ScriptError.UNABLE_TO_REVOKE_PROPOSAL);

                            break;
                        case Action.ACCEPT: // Accept a proposal from someone

                            message = wasInput(KeyValue.Get(
                                wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.MESSAGE)),
                                corradeCommandParameters.Message));

                            // Check whether a proposal has been sent.
                            postData = Web.wasGET(CORRADE_CONSTANTS.USER_AGENT,
                                "https://secondlife.com/my/account/partners.php",
                                new Dictionary<string, string>
                                {
                                    {"lang", "en-US"}
                                }, cookieContainer, corradeConfiguration.ServicesTimeout);

                            if (postData.Result == null)
                                throw new ScriptException(ScriptError.UNABLE_TO_REACH_PARTNERSHIP_PAGE);

                            doc = new HtmlDocument();
                            HtmlNode.ElementsFlags.Remove("form");
                            HtmlNode.ElementsFlags.Remove("option");
                            doc.LoadHtml(Encoding.UTF8.GetString(postData.Result));
                            revokeNode =
                                doc.DocumentNode.Descendants("a")
                                    .FirstOrDefault(o => o.Attributes["href"].Value.Equals("?revoke=true"));

                            if (revokeNode != null)
                                throw new ScriptException(ScriptError.PROPOSAL_ALREADY_SENT);

                            // Now accept the proposal.
                            formNode = doc.DocumentNode.SelectSingleNode("//form[@action='partners.php?lang=en']");

                            // Build the new partnership request form.
                            var acceptProposal = new Dictionary<string, string>();

                            // Get the token.
                            tokenNode = formNode.SelectSingleNode("//input[@name='CSRFToken']");
                            acceptProposal.Add(tokenNode.Attributes["name"].Value, tokenNode.Attributes["value"].Value);
                            // Get the partner.
                            partnerNode = formNode.SelectSingleNode("//input[@name='form[partner_id]']");
                            acceptProposal.Add(partnerNode.Attributes["name"].Value,
                                partnerNode.Attributes["value"].Value);
                            acceptProposal.Add("accept", "I Accept");

                            // Add proposal accept message.
                            acceptProposal.Add("form[reply]", message);

                            // Send the form.
                            postData = Web.wasPOST(CORRADE_CONSTANTS.USER_AGENT,
                                "https://secondlife.com/my/account/partners.php", acceptProposal,
                                CorradePOSTMediaType,
                                cookieContainer,
                                corradeConfiguration.ServicesTimeout);

                            if (postData.Result == null)
                                throw new ScriptException(ScriptError.UNABLE_TO_ACCEPT_PROPOSAL);

                            // Check for proposal errors (ie user already has a partner request, etc...).
                            doc = new HtmlDocument();
                            HtmlNode.ElementsFlags.Remove("form");
                            HtmlNode.ElementsFlags.Remove("option");
                            doc.LoadHtml(Encoding.UTF8.GetString(postData.Result));
                            errorNodes = doc.DocumentNode.SelectNodes("//div[@class='error']/ul/li");
                            if (errorNodes != null && errorNodes.Any())
                            {
                                result.Add(Reflection.GetNameFromEnumValue(ResultKeys.DATA),
                                    CSV.FromEnumerable(errorNodes.Select(o => o.InnerText.Trim())));
                                throw new ScriptException(ScriptError.UNABLE_TO_ACCEPT_PROPOSAL);
                            }

                            break;
                        case Action.REJECT: // Reject a proposal from someone.
                            message = wasInput(KeyValue.Get(
                                wasOutput(Reflection.GetNameFromEnumValue(ScriptKeys.MESSAGE)),
                                corradeCommandParameters.Message));

                            // Check whether a proposal has been sent.
                            postData = Web.wasGET(CORRADE_CONSTANTS.USER_AGENT,
                                "https://secondlife.com/my/account/partners.php",
                                new Dictionary<string, string>
                                {
                                    {"lang", "en-US"}
                                }, cookieContainer, corradeConfiguration.ServicesTimeout);

                            if (postData.Result == null)
                                throw new ScriptException(ScriptError.UNABLE_TO_REACH_PARTNERSHIP_PAGE);

                            doc = new HtmlDocument();
                            HtmlNode.ElementsFlags.Remove("form");
                            HtmlNode.ElementsFlags.Remove("option");
                            doc.LoadHtml(Encoding.UTF8.GetString(postData.Result));
                            revokeNode =
                                doc.DocumentNode.Descendants("a")
                                    .FirstOrDefault(o => o.Attributes["href"].Value.Equals("?revoke=true"));

                            if (revokeNode != null)
                                throw new ScriptException(ScriptError.PROPOSAL_ALREADY_SENT);

                            // Now accept the proposal.
                            formNode = doc.DocumentNode.SelectSingleNode("//form[@action='partners.php?lang=en']");

                            // Build the new partnership request form.
                            var rejectProposal = new Dictionary<string, string>();

                            // Get the token.
                            tokenNode = formNode.SelectSingleNode("//input[@name='CSRFToken']");
                            rejectProposal.Add(tokenNode.Attributes["name"].Value, tokenNode.Attributes["value"].Value);
                            // Get the partner.
                            partnerNode = formNode.SelectSingleNode("//input[@name='form[partner_id]']");
                            rejectProposal.Add(partnerNode.Attributes["name"].Value,
                                partnerNode.Attributes["value"].Value);
                            rejectProposal.Add("NoThanks", "No Thanks");

                            // Add proposal reject message.
                            rejectProposal.Add("form[reply]", message);

                            // Send the form.
                            postData = Web.wasPOST(CORRADE_CONSTANTS.USER_AGENT,
                                "https://secondlife.com/my/account/partners.php", rejectProposal,
                                CorradePOSTMediaType,
                                cookieContainer,
                                corradeConfiguration.ServicesTimeout);

                            if (postData.Result == null)
                                throw new ScriptException(ScriptError.UNABLE_TO_REJECT_PROPOSAL);

                            // Check for proposal errors (ie user already has a partner request, etc...).
                            doc = new HtmlDocument();
                            HtmlNode.ElementsFlags.Remove("form");
                            HtmlNode.ElementsFlags.Remove("option");
                            doc.LoadHtml(Encoding.UTF8.GetString(postData.Result));
                            errorNodes = doc.DocumentNode.SelectNodes("//div[@class='error']/ul/li");
                            if (errorNodes != null && errorNodes.Any())
                            {
                                result.Add(Reflection.GetNameFromEnumValue(ResultKeys.DATA),
                                    CSV.FromEnumerable(errorNodes.Select(o => o.InnerText.Trim())));
                                throw new ScriptException(ScriptError.UNABLE_TO_REJECT_PROPOSAL);
                            }

                            break;
                        default:
                            throw new ScriptException(ScriptError.UNKNOWN_ACTION);
                    }
                };
        }
    }
}