﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DokCapture.ServicenNetFramework.Auth;
using DokCapture.ServicenNetFramework.Auth.Models;
using Services.AuthService.Models;
using Dokmee.Dms.Connector.Advanced;
using Dokmee.Dms.Connector.Advanced.Core.Data;
using Dokmee.Dms.Connector.Advanced.Extension;
using Repositories;
using Services.SessionHelperService;
using Services.TempDbService;
using Dokmee.Dms.Advanced.WebAccess.Data;

namespace Services.AuthService
{
	public class DokmeeService : IDokmeeService
	{
		private ISessionHelperService _sessionHelperService;
		private DmsConnector _dmsConnector;
		private ConnectorModel _connectorModel;
		private ITempDbService _tempDbService;

		public DokmeeService(ISessionHelperService sessionHelperService, ITempDbService tempDbService)
		{
			_sessionHelperService = sessionHelperService;
			_tempDbService = tempDbService;
		}

		private ConnectorModel ConnectorVm
		{
			get
			{
				if (_connectorModel == null)
				{
					_connectorModel = new ConnectorModel();
					ConnectorVm.SelectedConnectorType = ConnectorVm.ConnectorTypes.First();
				}
				return _connectorModel;
			}
		}

		public Task<SignInResult> Login(string username, string password, ConnectorType type)
		{
			DokUser user = new DokUser();
			SignInResult result = SignInResult.Success;

			if (_dmsConnector == null)
			{
				CreateConnector(username, password, type);
			}

			return Task.FromResult(result);
		}

		public IEnumerable<DokmeeCabinet> GetCurrentUserCabinet(string username)
		{
			UserLogin user = _tempDbService.GetUserLogin(username);
			if (user == null)
			{
				throw new Exception("User login is not save to database.");
			}

			if (_dmsConnector == null)
			{
				var cabinets = CreateConnector(user.Username, user.Password, (ConnectorType)user.Type);
				return cabinets.DokmeeCabinets;
			}

			var loginResult = _dmsConnector.Login(new LogonInfo
			{
				Username = user.Username,
				Password = user.Password
			});
			return loginResult.DokmeeCabinets;
		}

		public IEnumerable<DmsNode> GetCabinetContent(string cabinetId, string username)
		{
			if (string.IsNullOrWhiteSpace(cabinetId))
			{
				throw new ArgumentException("cabinetId is null or empty");
			}

			if (string.IsNullOrWhiteSpace(username))
			{
				throw new ArgumentException("username is null or empty");
			}

			UserLogin user = _tempDbService.GetUserLogin(username);
			if (_dmsConnector == null)
			{
				CreateConnector(user.Username, user.Password, (ConnectorType)user.Type);
			}

			_dmsConnector.RegisterCabinet(new Guid(cabinetId));
			IEnumerable<DmsNode> dmsNodes = _dmsConnector.GetFsNodesByName();
			return dmsNodes;
		}

		public Task<IEnumerable<DmsNode>> GetFolderContent(string username, string id, bool isRoot)
		{
			UserLogin user = _tempDbService.GetUserLogin(username);
			if (_dmsConnector == null)
			{
				CreateConnector(user.Username, user.Password, (ConnectorType)user.Type);
			}
			IEnumerable<DmsNode> result = new List<DmsNode>();
			result = isRoot ? _dmsConnector.GetFilesystem(SubjectTypes.Folder)
							: _dmsConnector.GetFilesystem(SubjectTypes.Folder, id);

			return Task.FromResult(result);
		}

		public IEnumerable<DokmeeFilesystem> GetDokmeeFilesystems(string username, string name, bool isFolder, string cabinetId)
		{
			if (string.IsNullOrWhiteSpace(username))
			{
				throw new ArgumentException("username is null or empty");
			}
			UserLogin user = _tempDbService.GetUserLogin(username);
			IEnumerable<DokmeeFilesystem> results = new List<DokmeeFilesystem>();
			if (_dmsConnector == null)
			{
				CreateConnector(user.Username, user.Password, (ConnectorType)user.Type);
			}
			Guid id = Guid.Empty;
			if (!string.IsNullOrEmpty(cabinetId) && Guid.TryParse(cabinetId, out id))
			{
				_dmsConnector.RegisterCabinet(id);
				if (isFolder)
				{
					results = _dmsConnector.Search(SearchFieldType.TextIndex, name, "Folder Title").DmsFilesystem;
				}
				else
				{
					try
					{
						var nodes = _dmsConnector.GetFsNodesByName(SubjectTypes.Document, name);
						if (nodes != null && nodes.Any())
						{
							var nodeId = nodes.First()?.ID.ToString();
							if (!string.IsNullOrEmpty(nodeId))
							{
								results = _dmsConnector.Search(SearchFieldType.ByNodeID, nodeId).DmsFilesystem;
							}
						}
					}
					catch { }
				}
			}
			return results;
		}

		public void UpdateIndex(string username, string nodeID, IEnumerable<DokmeeIndex> dokmeeIndex, string cabinetId)
		{
			if (string.IsNullOrWhiteSpace(username))
			{
				throw new ArgumentException("username is null or empty");
			}
			UserLogin user = _tempDbService.GetUserLogin(username);
			IEnumerable<DokmeeFilesystem> results = new List<DokmeeFilesystem>();
			if (_dmsConnector == null)
			{
				CreateConnector(user.Username, user.Password, (ConnectorType)user.Type);
			}
			Guid id = Guid.Empty;
			Guid idTemp = Guid.Empty;
			if (!string.IsNullOrEmpty(nodeID) && Guid.TryParse(nodeID, out id)
				&& !string.IsNullOrEmpty(cabinetId) && Guid.TryParse(cabinetId, out idTemp))
			{
				_dmsConnector.RegisterCabinet(idTemp);
				_dmsConnector.UpdateIndex(id, dokmeeIndex);
			}
		}

		private DokmeeCabinetResult CreateConnector(string username, string password, ConnectorType type)
		{
			username = username ?? _sessionHelperService.Username;
			if (string.IsNullOrWhiteSpace(username))
			{
				throw new InvalideUsernameException("Username is null");
			}
			password = password ?? _sessionHelperService.Password;
			if (string.IsNullOrWhiteSpace(password))
			{
				throw new InvalidePasswordException("Password is null");
			}
			//// initialize connector
			DokmeeApplication dApp = DokmeeApplication.DokmeeDMS;

			if (type == ConnectorType.DMS)
			{
				ConnectionInfo connInfo = new ConnectionInfo
				{
					ServerName = ConnectorVm.Server,
					UserID = "sa",
					Password = "123456"
				};

				// register connection
				dApp = DokmeeApplication.DokmeeDMS;
				_dmsConnector = new DmsConnector(dApp);
				_dmsConnector.RegisterConnection<ConnectionInfo>(connInfo); // =>> this code cause lost session. Why???
			}
			else if (type == ConnectorType.WEB)
			{
				// register connection
				dApp = DokmeeApplication.DokmeeWeb;
				_dmsConnector = new DmsConnector(dApp);
				_dmsConnector.RegisterConnection<string>(ConnectorVm.HostUrl);
			}
			else if (type == ConnectorType.CLOUD)
			{
				// register connection
				dApp = DokmeeApplication.DokmeeCloud;
				_dmsConnector = new DmsConnector(dApp);
				_dmsConnector.RegisterConnection<string>("https://www.dokmeecloud.com");
			}

			var loginResult = _dmsConnector.Login(new LogonInfo
			{
				Username = username,
				Password = password
			});

			return loginResult;
		}

	}

	public class InvalideUsernameException : ArgumentException
	{
		public InvalideUsernameException(string mesage) : base(mesage)
		{

		}
	}

	public class InvalidePasswordException : ArgumentException
	{
		public InvalidePasswordException(string mesage) : base(mesage)
		{

		}
	}
}
