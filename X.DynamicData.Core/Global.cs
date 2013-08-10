﻿using Microsoft.WindowsAzure.Storage;
using System;
using System.Collections.Generic;
using System.Data.Entity.Infrastructure;
using System.Data.Objects;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web;
using System.Web.DynamicData;

namespace X.DynamicData.Core
{
    /// <summary>
    /// Global application context
    /// </summary>
    [Serializable]
    public class Global
    {
        private static Global _context;
        private static MetaModel _metaModel;

        public String Title { get; set; }
       
        public String WebsiteUrl { get; set; }
        public String WebsiteStorageConnectionString { get; set; }

        public String FileStorageUrl { get; set; }
        public String FileStorageConnectionString { get; set; }

        public String DataContextAssemblyLocation { get; set; }
        public String Logo { get; set; }
        public String BlobContainerName { get; set; }
        public bool ScaffoldAllTables { get; set; }
        public bool ShowLogsInMenu { get; set; }

        public bool IsDebuggingEnabled
        {
            get { return HttpContext.Current.IsDebuggingEnabled; }
        }

        public Version Verison
        {
            get
            {
                var assembly = Assembly.GetEntryAssembly();

                if (assembly == null)
                {
                    if (HttpContext.Current == null || HttpContext.Current.ApplicationInstance == null)
                    {
                        return null;
                    }

                    var type = HttpContext.Current.ApplicationInstance.GetType();

                    while (type != null && type.Namespace == "ASP")
                    {
                        type = type.BaseType;
                    }

                    if (type != null)
                    {
                        assembly = type.Assembly;
                    }
                }

                if (assembly != null)
                {
                    var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
                    return Version.Parse(fileVersionInfo.ProductVersion);
                }

                return null;
            }
        }

        public Global()
        {
            ScaffoldAllTables = false;
        }

        public static MetaModel MetaModel
        {
            get { return _metaModel ?? (_metaModel = new MetaModel()); }
        }

        public static Global Context
        {
            get { return _context ?? (_context = Load(System.Web.Configuration.WebConfigurationManager.AppSettings)); }
        }
        
        private static Global Load(System.Collections.Specialized.NameValueCollection settings)
        {
            return new Global
                {
                    Title = settings["Title"],
                  
                    WebsiteUrl = settings["WebsiteUrl"],
                    WebsiteStorageConnectionString = settings["WebsiteStorageConnectionString"],

                    FileStorageConnectionString = settings["FileStorageConnectionString"],
                    FileStorageUrl = settings["FileStorageUrl"],

                    DataContextAssemblyLocation = settings["DataContextAssemblyLocation"],
                    Logo = settings["Logo"],
                    BlobContainerName = settings["BlobContainerName"],
                    ScaffoldAllTables = settings["ScaffoldAllTables"] == "true",
                    ShowLogsInMenu = settings["ShowLogsInMenu"] == "true",
                };
        }

        public static ObjectContext CreateDataContext(string path)
        {
            var types = GetTypes(path);

            var dataContextType = (from o in types
                                   where o.BaseType != null &&
                                         (o.BaseType.Name.Contains("DbContext") ||
                                          o.BaseType.Name.Contains("ObjectContext"))
                                   select o).FirstOrDefault();
            
            var instance = Activator.CreateInstance(dataContextType);

            if (instance.GetType().BaseType.Name == typeof(ObjectContext).Name)
            {
                return (ObjectContext)instance;
            }

            return ((IObjectContextAdapter)instance).ObjectContext;
        }

        private static IEnumerable<Type> GetTypes(string path)
        {
            if (!File.Exists(path))
            {
                return new List<Type>();
            }

            var assembly = Assembly.UnsafeLoadFrom(path);

            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                return e.Types.Where(t => t != null).ToList();
            }
        }

        /// <summary>
        /// Save file to website
        /// </summary>
        /// <param name="bytes">File content</param>
        /// <param name="name">Name for new file</param>
        /// <returns>url to file</returns>
        public static string UploadFile(byte[] bytes, string name)
        {
            name = name.ToLower();
            var url = String.Format("{0}{1}", Context.FileStorageUrl, name);

            var storageType = GetStorageType(Context.FileStorageConnectionString);

            switch (storageType)
            {
                case Storage.FileSystem:
                    {
                        var path = String.Format("{0}{1}", Global.Context.FileStorageConnectionString, name);
                        File.WriteAllBytes(path, bytes);
                        break;
                    }

                case Storage.WindowsAzureStorage:
                    {
                        //Upload to Windows Azure Storage
                        var storageAccount = CloudStorageAccount.Parse(Context.FileStorageConnectionString);

                        var blobClient = storageAccount.CreateCloudBlobClient();

                        // Retrieve a reference to a container. 
                        var container = blobClient.GetContainerReference(Context.BlobContainerName);

                        // Retrieve reference to a blob named "myblob".
                        var blockBlob = container.GetBlockBlobReference(name);

                        // Create or overwrite the blob with contents from a file.
                        var stream = new MemoryStream(bytes);
                        blockBlob.UploadFromStream(stream);

                        url = blockBlob.Uri.ToString();
                        break;
                    }

                case Storage.Ftp:
                    {
                        var path = Context.FileStorageConnectionString + name;
                        var ftp = new Ftp();
                        ftp.UploadFile(bytes, path);
                        break;
                    }
                case Storage.Unknown:
                    {
                        throw new Exception("Unknow storage type");
                        break;
                    }
            }

            return url;
        }

        public bool RestarWebApplication()
        {
            var storageType = GetStorageType(Context.WebsiteStorageConnectionString);

            try
            {
                switch (storageType)
                {
                    case Storage.FileSystem:
                        {
                            var path = Global.Context.WebsiteStorageConnectionString + "web.config";
                            var stream = File.Open(path, FileMode.Open);
                            var streamWriter = new StreamWriter(stream);
                            streamWriter.WriteLine("<!--restart-->");
                            stream.Close();
                            var text = File.ReadAllText(path).Replace("<!--restart-->", String.Empty);
                            File.WriteAllText(path, text);
                            break;
                        }
                    case Storage.WindowsAzureStorage:
                        {
                            throw new NotImplementedException();
                            break;
                        }
                    case Storage.Ftp:
                        {
                            var path = Context.WebsiteStorageConnectionString + "web.config";
                            var ftp = new Ftp();
                            var bytes = ftp.DownloadFile(path);
                            //var result = System.Text.Encoding.UTF8.GetString(bytes);
                            ftp.UploadFile(bytes, path);
                            break;
                        }
                    case Storage.Unknown:
                        {
                            throw new Exception("Unknow storage type");
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                return false;
            }

            return true;
        }

        private static Storage GetStorageType(string path)
        {
            if (String.IsNullOrEmpty(path))
            {
                return Storage.Unknown;
            }

            if (path.Contains("DefaultEndpointsProtocol"))
            {
                return Storage.WindowsAzureStorage;
            }

            if (path.Contains("@"))
            {
                return Storage.Ftp;
            }

            return Storage.FileSystem;
        }

        public static bool CanCreateDataContext()
        {
            try
            {
                Global.CreateDataContext(Global.Context.DataContextAssemblyLocation);
            }
            catch
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Return localize text
        /// </summary>
        /// <param name="resorceName"></param>
        /// <returns></returns>
        public static string GetText(string resorceName)
        {
            var resource = HttpContext.GetGlobalResourceObject("Global", resorceName);

            if (resource == null)
            {
                return String.Format("# {0} #", resorceName);
            }

            return resource.ToString();
        }
    }
}