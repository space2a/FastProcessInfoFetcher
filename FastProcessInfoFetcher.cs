using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Threading.Tasks;

namespace BetterTaskManager
{
    /// <summary>
    /// FastProcessInfoFetcher is using WMI and System.Diagnostics.Process.
    /// </summary>
    public static class FastProcessInfoFetcher
    {
        /// <summary>
        /// Will return a list of Windows processes (without any services) in a tree like structure, with process and their subprocesses.
        /// </summary>
        public static ProcessNode[] GetProcessesTreeStructure(string[] fetchArguments = null, bool excludeServices = true)
        {
            //Here, we are going to combine both the System.Diagnostics.Process.GetProcesses func and WMI Win32_Process.
            //GetProcesses() is way faster than Win32_Process however GetProcesses() is not going to give us processes parent ids.

            //We are going to enumerate all the process with only their PID and their parent PID, from WMI...

            List<Process> winProcesses = Process.GetProcesses().ToList();
            var services = GetServices().ToList();

            SelectQuery selectQuery = new SelectQuery("SELECT * FROM Win32_Process"); //Will also give us the services, we will ignore them later.
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(selectQuery);

            var data = searcher.Get();

            //Dictionary<int, ProcessNode> pNodes = new Dictionary<int, ProcessNode>(); //PPID, Processes
            List<ProcessNode> pNodes = new List<ProcessNode>();
            int s = 0;

            foreach (ManagementObject d in data)
            {
                int pid = int.Parse(d.Properties["ProcessId"].Value.ToString()); //Get the PID
                int ppid = int.Parse(d.Properties["parentprocessid"].Value.ToString()); //Get the parent PID

                string[] args = null;
                if (fetchArguments != null)
                {
                    args = new string[fetchArguments.Length];
                    for (int i = 0; i < fetchArguments.Length; i++)
                    {
                        args[i] = d.Properties[fetchArguments[i]].Value?.ToString();
                    }
                }

                var serv = services.Find(x => x.Id == pid);
                if (serv != null)
                {
                    services.Remove(serv);

                    if (!excludeServices)
                    {
                        pNodes.Add(new ProcessNode()
                        {
                            PPID = ppid,
                            PID = pid,
                            Process = serv,
                            IsService = true,
                            FetchArguments = args
                        });
                    }

                    continue;
                }

                Process processReference = winProcesses.Find(x => x.Id == pid);
                //if (processReference == null) continue; //If null the current process is a service

                pNodes.Add(new ProcessNode() { PPID = ppid, PID = pid, Process = processReference, FetchArguments = args });
            }

            //Now that the subprocesses are together, we must assign them a parent.

            List<ProcessNode> keysToRemove = new List<ProcessNode>();
            for (int i = 0; i < pNodes.Count; i++)
            {
                int ind = pNodes.FindIndex(x => x.PID == pNodes[i].PPID && x.Process?.ProcessName != "explorer" && x.IsService == pNodes[i].IsService);

                if (ind != -1)
                {
                    pNodes[ind].ProcessNodeChild.Add(pNodes[i]);
                    keysToRemove.Add(pNodes[i]);
                    continue;
                }
            }

            //Removing unwanted nodes
            for (int i = 0; i < keysToRemove.Count; i++)
                pNodes.Remove(keysToRemove[i]);

            keysToRemove.Clear();

            return pNodes.ToArray();
        }


        /// <summary>
        /// Will only return Windows processes, without any Windows services.
        /// </summary>
        /// <returns></returns>
        public static Process[] GetProcesses()
        {
            var services = GetServices();

            List<Process> processes = Process.GetProcesses().ToList();

            for (int i = 0; i < services.Length; i++)
            {
                var p = processes.Find(x => x.Id == services[i].Id);
                if (p != null)
                    processes.Remove(p);
            }

            return processes.ToArray();
        }

        /// <summary>
        /// Will only return Windows services, without any Windows process.
        /// </summary>
        /// <returns></returns>
        public static Process[] GetServices()
        {
            DateTime dateTime = DateTime.Now;

            SelectQuery selectQuery = new SelectQuery("SELECT * FROM Win32_Service");
            ManagementObjectSearcher searcher = new ManagementObjectSearcher(selectQuery);

            var data = searcher.Get();

            List<Process> processes = Process.GetProcesses().ToList();

            Process[] services = new Process[data.Count];
            int i = 0;
            foreach (ManagementObject d in data)
            {
                int pid = int.Parse(d.Properties["ProcessId"].Value.ToString());
                services[i++] = processes.Find(x => x.Id == pid);
            }

            return services;
        }
    }

    public class ProcessNode
    {
        public int PPID { get; set; }
        public int PID { get; set; }
        public Process Process { get; set; }
        public List<ProcessNode> ProcessNodeChild { get; set; } = new List<ProcessNode>();

        public bool IsService;

        public string[] FetchArguments;

        public override string ToString()
        {
            string n = "";

            foreach (var c in ProcessNodeChild)
                n += c.ToString() + " ";

            return Process?.ProcessName + " #" + Process?.Id +  " (" + ProcessNodeChild?.Count.ToString() + ") " + n;
        }
    }
}
