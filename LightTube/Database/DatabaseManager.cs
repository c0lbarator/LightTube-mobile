using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using MongoDB.Driver;
using Newtonsoft.Json;

namespace LightTube.Database
{
	public static class DatabaseManager
	{
		public static readonly string ApiUaRegex = "LightTubeApiClient\\/([0-9.]*) ([\\S]+?)\\/([0-9.]*) \\(([\\s\\S]+?)\\)";

		private static IMongoCollection<LTUser> _userCollection;
		private static IMongoCollection<LTLogin> _tokenCollection;
		private static IMongoCollection<LTChannel> _channelCacheCollection;
		public static LoginManager Logins { get; private set; }
		public static ChannelManager Channels { get; private set; }

		public static void Init(string connstr)
		{
			MongoClient client = new(connstr);
			IMongoDatabase database = client.GetDatabase("lighttube");
			_userCollection = database.GetCollection<LTUser>("users");
			_tokenCollection = database.GetCollection<LTLogin>("tokens");
			_channelCacheCollection = database.GetCollection<LTChannel>("channelCache");
			Logins = new LoginManager(_userCollection, _tokenCollection);
			Channels = new ChannelManager(_channelCacheCollection);
		}

		public static void CreateLocalAccount(this HttpContext context)
		{
			bool accountExists = false;

			// Check local account
			if (context.Request.Cookies.TryGetValue("account_data", out string accountJson))
			{
				try
				{
					if (accountJson != null)
					{
						LTUser tempUser = JsonConvert.DeserializeObject<LTUser>(accountJson) ?? new LTUser();
						if (tempUser.Email == "Local Account" && tempUser.PasswordHash == "local_account")
							accountExists = true;
					}
				}
				catch { }
			}

			// Account already exists, just leave it there
			if (accountExists) return;

			LTUser user = new()
			{
				Email = "Local Account",
				PasswordHash = "local_account",
				SubscribedChannels = new List<string>()
			};

			context.Response.Cookies.Append("account_data", JsonConvert.SerializeObject(user), new CookieOptions
			{
				Expires = DateTimeOffset.MaxValue 
			});
		}

		public static bool TryGetUser(this HttpContext context, out LTUser user, string requiredScope)
		{
			// Check local account
			if (context.Request.Cookies.TryGetValue("account_data", out string accountJson))
			{
				try
				{
					if (accountJson != null)
					{
						LTUser tempUser = JsonConvert.DeserializeObject<LTUser>(accountJson) ?? new LTUser();
						if (tempUser.Email == "Local Account" && tempUser.PasswordHash == "local_account")
						{
							user = tempUser;
							return true;
						}
					}
				}
				catch
				{
					user = null;
					return false;
				}
			}
			
			// Check cloud account
			if (!context.Request.Cookies.TryGetValue("token", out string token))
				if (context.Request.Headers.TryGetValue("Authorization", out StringValues tokens))
					token = tokens.ToString();
				else
				{
					user = null;
					return false;
				}

			try
			{
				if (token != null)
				{
					user = Logins.GetUserFromToken(token).Result;
					LTLogin login = Logins.GetLoginFromToken(token).Result;
					if (login.Scopes.Contains(requiredScope))
					{
#pragma warning disable 4014
						login.UpdateLastAccess(DateTimeOffset.Now);
#pragma warning restore 4014
						return true;
					}
					return false;
				}
			}
			catch
			{
				user = null;
				return false;
			}

			user = null;
			return false;
		}
	}
}