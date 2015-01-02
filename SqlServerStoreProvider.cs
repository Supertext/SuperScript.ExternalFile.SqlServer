using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SqlClient;
using SuperScript.ExternalFile.Storage;

namespace SuperScript.ExternalFile.SqlServer
{
	public class SqlServerStoreProvider : IDbStoreProvider
	{
		#region Global variables

		private bool _tableExists;

		#endregion


		#region Properties

		/// <summary>
		/// Gets or sets the connection string that will be used to communicate with the underlying database.
		/// </summary>
		public string ConnectionString { get; set; }


		/// <summary>
		/// Gets or sets the name of the database, if this is not already detailed in the connection string.
		/// </summary>
		public string DbName { get; set; }


		/// <summary>
		/// Gets or sets the name of the table inside the database.
		/// </summary>
		public string StoreName { get; set; }

		#endregion


		#region Methods

		/// <summary>
		/// Adds the specified instance of <see cref="IStorable"/> to the store using the specified key. If an 
		/// item exists with the specified <see cref="IStorable.Key"/> then it will be updated.
		/// </summary>
		/// <param name="storable">An instance of <see cref="IStorable"/> which contains all pertinent data.</param>
		/// <exception cref="ArgumentException">Thrown if the <see cref="IStorable.Key"/> property is null or whitespace.</exception>
		/// <exception cref="ConfigurablePropertyNotSpecifiedException">The <see cref="ConnectionString"/> has not been populated.</exception>
		/// <exception cref="ConfigurablePropertyNotSpecifiedException">Thrown if <see cref="StoreName"/> is null or whitespace.</exception>
		public void AddOrUpdate(IStorable storable)
		{
			// verify that a valid key has been specified
			if (String.IsNullOrWhiteSpace(storable.Key))
			{
				throw new ArgumentException("The key parameter must be a non-zero-length string.");
			}

			if (ConnectionString == null)
			{
				throw new ConfigurablePropertyNotSpecifiedException("The ConnectionString property must be specified.");
			}

			// verify that we have a StoreName
			if (String.IsNullOrWhiteSpace(StoreName))
			{
				throw new ConfigurablePropertyNotSpecifiedException("The StoreName property must be specified.");
			}

			// execute the INSERT query
			using (var conn = new SqlConnection(ConnectionString))
			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandType = CommandType.Text;
				cmd.CommandText = @"IF EXISTS (SELECT * FROM " + StoreName + @" WITH (UPDLOCK, SERIALIZABLE) WHERE [key] = @key)
									BEGIN
										UPDATE [dbo].[" + StoreName + @"]
										SET
											[key] = @key,
											cacheForTimePeriod = @cacheForTimePeriod,
											contents = @contents,
											contentType = @contentType,
											longevity = @longevity
										WHERE
											[key] = @key
									END
									ELSE
									BEGIN
										INSERT INTO [dbo].[" + StoreName + @"]
												   ([key],
													cacheForTimePeriod,
													contents,
													contentType,
													longevity)
											 VALUES
													(@key,
													@cacheForTimePeriod,
													@contents,
													@contentType,
													@longevity)
									END";
				cmd.Parameters.AddWithValue("@key", storable.Key);
				cmd.Parameters.AddWithValue("@cacheForTimePeriod", storable.CacheForTimePeriod_Serialize);
				cmd.Parameters.AddWithValue("@contents", storable.Contents);
				cmd.Parameters.AddWithValue("@contentType", storable.ContentType);
				cmd.Parameters.AddWithValue("@longevity", (int) storable.Longevity);

				conn.Open();
				if (!String.IsNullOrWhiteSpace(DbName))
				{
					conn.ChangeDatabase(DbName);
				}

				cmd.ExecuteNonQuery();
			}
		}


		/// <summary>
		/// Contains the instructions for initialising a SQL Server-based implementation of <see cref="IStore"/>.
		/// </summary>
		/// <returns><c>True</c> if the store was created, <c>false</c> otherwise.</returns>
		/// <exception cref="MissingDatabaseConfigurationException">The <see cref="ConnectionString"/> has not been populated.</exception>
		/// <exception cref="ConfigurablePropertyNotSpecifiedException">Thrown if <see cref="StoreName"/> is null or whitespace.</exception>
		private bool CreateStore()
		{
			if (ConnectionString == null)
			{
				throw new MissingDatabaseConfigurationException("No matching connection string was found for the specified ConnectionStringName.");
			}

			// verify that we have a StoreName
			if (String.IsNullOrWhiteSpace(StoreName))
			{
				throw new ConfigurablePropertyNotSpecifiedException("The StoreName property must be specified.");
			}

			using (var conn = new SqlConnection(ConnectionString))
			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandType = CommandType.Text;
				cmd.CommandText = @"SET ANSI_NULLS ON
										SET QUOTED_IDENTIFIER ON
										SET ANSI_PADDING ON
										IF OBJECT_ID('dbo." + StoreName + @"', 'U') IS NOT NULL DROP TABLE dbo." + StoreName + @"
										CREATE TABLE [dbo].[" + StoreName + @"](
											[key] [nvarchar](250) NOT NULL,
											[cacheForTimePeriod] [varchar](20) NOT NULL,
											[contents] [text] NOT NULL,
											[contentType] [varchar](45) NOT NULL,
											[longevity] [int] NOT NULL,
										 CONSTRAINT [PK_superscript_externalfiles] PRIMARY KEY CLUSTERED 
										(
											[key] ASC
										)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
										) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
										SET ANSI_PADDING OFF
										ALTER TABLE [dbo].[" + StoreName + @"] ADD  CONSTRAINT [DF_" + StoreName + @"_cacheForTimePeriod]  DEFAULT ('{0:00:00:00}') FOR [cacheForTimePeriod]
										ALTER TABLE [dbo].[" + StoreName + @"] ADD  CONSTRAINT [DF_" + StoreName + @"_longevity]  DEFAULT ((0)) FOR [longevity]
										SELECT
											* 
										FROM
											INFORMATION_SCHEMA.TABLES 
										WHERE
											TABLE_NAME = '" + StoreName + "'";

				conn.Open();
				if (!String.IsNullOrWhiteSpace(DbName))
				{
					conn.ChangeDatabase(DbName);
				}

				using (var rdr = cmd.ExecuteReader())
				{
					return rdr.HasRows;
				}
			}
		}


		/// <summary>
		/// Deletes the instance of <see cref="IStorable"/> which has been stored against the specified <see cref="key"/>.
		/// </summary>
		/// <param name="key">The unique identifier that the <see cref="IStorable"/> was stored under.</param>
		/// <exception cref="ArgumentException">Thrown if the <see cref="key"/> property is null or whitespace.</exception>
		/// <exception cref="MissingDatabaseConfigurationException">The <see cref="ConnectionString"/> has not been populated.</exception>
		/// <exception cref="ConfigurablePropertyNotSpecifiedException">Thrown if <see cref="StoreName"/> is null or whitespace.</exception>
		public void Delete(string key)
		{
			// verify that a valid key has been specified
			if (String.IsNullOrWhiteSpace(key))
			{
				throw new ArgumentException("The key parameter must be a non-zero-length string.");
			}

			if (ConnectionString == null)
			{
				throw new MissingDatabaseConfigurationException("No matching connection string was found for the specified ConnectionStringName.");
			}

			// verify that we have a StoreName
			if (String.IsNullOrWhiteSpace(StoreName))
			{
				throw new ConfigurablePropertyNotSpecifiedException("The StoreName property must be specified.");
			}

			// execute the DELETE query
			using (var conn = new SqlConnection(ConnectionString))
			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandType = CommandType.Text;
				cmd.CommandText = @"DELETE FROM [dbo].[" + StoreName + @"]
									WHERE [key] = @key";
				cmd.Parameters.AddWithValue("@key", key);

				conn.Open();
				if (!String.IsNullOrWhiteSpace(DbName))
				{
					conn.ChangeDatabase(DbName);
				}

				cmd.ExecuteNonQuery();
			}
		}


		/// <summary>
		/// Deletes the entire store from the database.
		/// </summary>
		/// <exception cref="MissingDatabaseConfigurationException">The <see cref="ConnectionString"/> has not been populated.</exception>
		/// <exception cref="ConfigurablePropertyNotSpecifiedException">Thrown if <see cref="StoreName"/> is null or whitespace.</exception>
		public void DeleteStore()
		{
			if (ConnectionString == null)
			{
				throw new MissingDatabaseConfigurationException("No matching connection string was found for the specified ConnectionStringName.");
			}

			// verify that we have a StoreName
			if (String.IsNullOrWhiteSpace(StoreName))
			{
				throw new ConfigurablePropertyNotSpecifiedException("The StoreName property must be specified.");
			}

			using (var conn = new SqlConnection(ConnectionString))
			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandType = CommandType.Text;
				cmd.CommandText = @"IF OBJECT_ID('dbo." + StoreName + @"', 'U') IS NOT NULL DROP TABLE dbo." + StoreName + @"";

				conn.Open();
				if (!String.IsNullOrWhiteSpace(DbName))
				{
					conn.ChangeDatabase(DbName);
				}

				cmd.ExecuteNonQuery();
			}

			_tableExists = false;
		}


		/// <summary>
		/// <para>Gets the instance of <see cref="IStorable"/> with the specified key.</para>
		/// <para>Returns null if no matching keys were found.</para>
		/// </summary>
		/// <param name="key">The unique identifier that the <see cref="IStorable"/> was stored under.</param>
		/// <exception cref="ArgumentException">Thrown if the <see cref="key"/> property is null or whitespace.</exception>
		/// <exception cref="MissingDatabaseConfigurationException">The <see cref="ConnectionString"/> has not been populated.</exception>
		/// <exception cref="ConfigurablePropertyNotSpecifiedException">Thrown if <see cref="StoreName"/> is null or whitespace.</exception>
		public IStorable Get(string key)
		{
			// verify that a valid key has been specified
			if (String.IsNullOrWhiteSpace(key))
			{
				throw new ArgumentException("The key parameter must be a non-zero-length string.");
			}

			if (ConnectionString == null)
			{
				throw new MissingDatabaseConfigurationException("No matching connection string was found for the specified ConnectionStringName.");
			}

			// verify that we have a StoreName
			if (String.IsNullOrWhiteSpace(StoreName))
			{
				throw new ConfigurablePropertyNotSpecifiedException("The StoreName property must be specified.");
			}

			using (var conn = new SqlConnection(ConnectionString))
			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandType = CommandType.Text;
				cmd.CommandText = @"SELECT
										cacheForTimePeriod,	-- 0
										contents,			-- 1
										contentType,		-- 2		
										longevity			-- 3
									FROM
										[dbo].[" + StoreName + @"]
									WHERE
										[key] = @key;";
				cmd.Parameters.AddWithValue("@key", key);

				conn.Open();
				if (!String.IsNullOrWhiteSpace(DbName))
				{
					conn.ChangeDatabase(DbName);
				}

				using (var rdr = cmd.ExecuteReader())
				{
					if (!rdr.Read())
					{
						return null;
					}

					var storable = new Storable
						               {
							               Key = key,
							               CacheForTimePeriod_Serialize = rdr.GetString(0),
							               Contents = rdr.GetString(1),
							               ContentType = rdr.GetString(2)
						               };

					Longevity lgvty;
					if (Enum.TryParse(rdr.GetInt32(3).ToString(), out lgvty))
					{
						storable.Longevity = lgvty;
					}
					else
					{
						throw new InvalidEnumArgumentException("The value used for Longevity was not valid");
					}

					return storable;
				}
			}
		}


		/// <summary>
		/// Returns a snapshot of all <see cref="IStorable"/> instances in the store.
		/// </summary>
		/// <exception cref="MissingDatabaseConfigurationException">The <see cref="ConnectionString"/> has not been populated.</exception>
		/// <exception cref="ConfigurablePropertyNotSpecifiedException">Thrown if <see cref="StoreName"/> is null or whitespace.</exception>
		public IEnumerable<IStorable> GetAll()
		{
			if (ConnectionString == null)
			{
				throw new MissingDatabaseConfigurationException("No matching connection string was found for the specified ConnectionStringName.");
			}

			// verify that we have a StoreName
			if (String.IsNullOrWhiteSpace(StoreName))
			{
				throw new ConfigurablePropertyNotSpecifiedException("The StoreName property must be specified.");
			}

			using (var conn = new SqlConnection(ConnectionString))
			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandType = CommandType.Text;
				cmd.CommandText = @"SELECT
										[key],				-- 0
										cacheForTimePeriod,	-- 1
										contents,			-- 2
										contentType,		-- 3		
										longevity			-- 4
									FROM
										[dbo].[" + StoreName + @"];";

				conn.Open();
				if (!String.IsNullOrWhiteSpace(DbName))
				{
					conn.ChangeDatabase(DbName);
				}

				using (var rdr = cmd.ExecuteReader())
				{
					var storables = new List<Storable>();

					while (rdr.Read())
					{
						var storable = new Storable
							               {
											   Key = rdr.GetString(0),
								               CacheForTimePeriod_Serialize = rdr.GetString(1),
								               Contents = rdr.GetString(2),
								               ContentType = rdr.GetString(3)
							               };

						Longevity lgvty;
						if (Enum.TryParse(rdr.GetInt32(4).ToString(), out lgvty))
						{
							storable.Longevity = lgvty;
						}
						else
						{
							throw new InvalidEnumArgumentException("The value used for Longevity was not valid");
						}

						storables.Add(storable);
					}

					return storables;
				}
			}
		}


		/// <summary>
		/// Checks that the store (a database table) exists. If not, the store will be created.
		/// </summary>
		/// <exception cref="MissingDatabaseConfigurationException">The <see cref="ConnectionString"/> has not been populated.</exception>
		/// <exception cref="ConfigurablePropertyNotSpecifiedException">Thrown if <see cref="StoreName"/> is null or whitespace.</exception>
		public void Init()
		{
			_tableExists = StoreExists();

			if (_tableExists)
			{
				return;
			}

			_tableExists = CreateStore();
			if (!_tableExists)
			{
				throw new UnableToCreateStoreException();
			}
		}


		/// <summary>
		/// Indicates whether the store currently exists.
		/// </summary>
		/// <returns><c>True</c> if the store was created, <c>false</c> otherwise.</returns>
		/// <exception cref="MissingDatabaseConfigurationException">The <see cref="ConnectionString"/> has not been populated.</exception>
		/// <exception cref="ConfigurablePropertyNotSpecifiedException">Thrown if <see cref="StoreName"/> is null or whitespace.</exception>
		private bool StoreExists()
		{
			if (ConnectionString == null)
			{
				throw new MissingDatabaseConfigurationException("No matching connection string was found for the specified ConnectionStringName.");
			}

			// verify that we have a StoreName
			if (String.IsNullOrWhiteSpace(StoreName))
			{
				throw new ConfigurablePropertyNotSpecifiedException("The StoreName property must be specified.");
			}

			using (var conn = new SqlConnection(ConnectionString))
			using (var cmd = conn.CreateCommand())
			{
				cmd.CommandType = CommandType.Text;
				cmd.CommandText = @"SELECT
										* 
									FROM
										INFORMATION_SCHEMA.TABLES 
									WHERE
										TABLE_NAME = '" + StoreName + "'";

				conn.Open();
				if (!String.IsNullOrWhiteSpace(DbName))
				{
					conn.ChangeDatabase(DbName);
				}

				using (var rdr = cmd.ExecuteReader())
				{
					return rdr.HasRows;
				}
			}
		}

		#endregion
	}
}