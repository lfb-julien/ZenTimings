using AdonisUI.Extensions;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using ZenStates.Core;
using ZenStates.Core.DRAM;
using static ZenTimings.BiosMemController;

namespace ZenTimings.Windows
{
    /// <summary>
    /// Interaction logic for SystemInfoWindow.xaml
    /// </summary>
    public partial class SystemInfoWindow
    {
        private class GridItem
        {
            public string Name { get; set; }
            public string Value { get; set; }
        }

        public SystemInfoWindow(MemoryConfig mc, Resistances? mcConfig, List<AsusSensorInfo> asusSensors)
        {
            InitializeComponent();
            SystemInfo si = CpuSingleton.Instance.systemInfo;
            AodData aodData = CpuSingleton.Instance.info.aod.Table.Data;
            Type type = si.GetType();
            PropertyInfo[] properties = type.GetProperties();
            List<GridItem> items;

            // ====================== SYSTEM INFO ======================
            try
            {
                items = new List<GridItem>
                {
                    new GridItem() {Name = "OS", Value = new Microsoft.VisualBasic.Devices.ComputerInfo().OSFullName}
                };

                foreach (PropertyInfo property in properties)
                {
                    if (property.Name == "CpuId" || property.Name == "PatchLevel" || property.Name == "SmuTableVersion")
                        items.Add(new GridItem() { Name = property.Name, Value = $"{property.GetValue(si, null):X8}" });
                    else if (property.Name == "SmuVersion")
                        items.Add(new GridItem() { Name = property.Name, Value = si.GetSmuVersionString() });
                    else
                        items.Add(new GridItem()
                        { Name = property.Name, Value = property.GetValue(si, null).ToString() });
                }

                TestGrid.ItemsSource = items;
            }
            catch
            {
                // ignored
            }

            // ====================== MEMORY TIMINGS GRID ======================
            try
            {
                var memConfigs = CpuSingleton.Instance.GetMemoryConfig();
                var allTimings = memConfigs.Timings;
                var props = allTimings[0].Value.GetType().GetProperties();

                // Filter timings to only include unique DctOffset values
                var uniqueTimings = allTimings
                    .GroupBy(t => t.Key)
                    .Select(g => g.First())
                    .ToList();

                // Create dynamic object with properties for each timing column
                var rows = props
                    .Where(p => p.Name != "Item")
                    .Select(property => new
                    {
                        PropertyName = property.Name,
                        Values = uniqueTimings.Select(t => t.Value[property.Name].ToString()).ToArray()
                    })
                    .ToList();

                MemCfgGrid.ItemsSource = rows;

                // Ensure columns exist for each unique timing
                if (MemCfgGrid.Columns.Count < uniqueTimings.Count + 1)
                {
                    MemCfgGrid.Columns.Clear();

                    // Add property name column with default text color
                    var nameColumn = new System.Windows.Controls.DataGridTextColumn
                    {
                        Header = "Name",
                        Binding = new System.Windows.Data.Binding("PropertyName"),
                        Width = 150
                    };
                    nameColumn.ElementStyle = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
                    nameColumn.ElementStyle.Setters.Add(
                        new System.Windows.Setter(
                            System.Windows.Controls.TextBlock.ForegroundProperty,
                            (System.Windows.Media.Brush)this.FindResource("TextColor")));
                    MemCfgGrid.Columns.Add(nameColumn);

                    // Add column for each unique timing with accent text color
                    for (int i = 0; i < uniqueTimings.Count; i++)
                    {
                        var valueColumn = new System.Windows.Controls.DataGridTextColumn
                        {
                            Header = $"DCT {uniqueTimings[i].Key >> 20}",
                            Binding = new System.Windows.Data.Binding($"Values[{i}]")
                        };
                        valueColumn.ElementStyle = new System.Windows.Style(typeof(System.Windows.Controls.TextBlock));
                        valueColumn.ElementStyle.Setters.Add(
                            new System.Windows.Setter(
                                System.Windows.Controls.TextBlock.ForegroundProperty,
                                (System.Windows.Media.Brush)this.FindResource("AccentTextColor")));
                        MemCfgGrid.Columns.Add(valueColumn);
                    }
                }
            }
            catch
            {
                // ignored
            }

            // ====================== MEM CONTROLLER / AOD ======================
            if (mcConfig != null && mc.Type == MemoryConfig.MemType.DDR4)
            {
                try
                {
                    type = mcConfig.GetType();
                    FieldInfo[] fields = type.GetFields();
                    items = new List<GridItem>();
                    foreach (FieldInfo property in fields)
                        items.Add(new GridItem() { Name = property.Name, Value = property.GetValue(mcConfig).ToString() });

                    MemControllerGrid.ItemsSource = items;
                }
                catch
                {
                    // ignored
                }
            }
            else
            {
                try
                {
                    properties = aodData.GetType().GetProperties();
                    items = new List<GridItem>();
                    foreach (PropertyInfo property in properties)
                    {
                        object value = property.GetValue(aodData);
                        items.Add(new GridItem() { Name = property.Name, Value = $"{value}" });
                    }

                    MemControllerGrid.ItemsSource = items;
                }
                catch
                {
                    // ignored
                }
            }

            //AsusWmiGrid.ItemsSource = asusSensors;

            DataContext = new
            {
                asusSensors
            };
        }

        private void AdonisWindow_Activated(object sender, EventArgs e)
        {
            InteropMethods.EmptyWorkingSet(System.Diagnostics.Process.GetCurrentProcess().Handle);
        }

        private void AdonisWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            AppSettings appSettings = AppSettings.Instance;
            if (appSettings.SaveWindowPosition)
            {
                appSettings.SysInfoWindowLeft = Left;
                appSettings.SysInfoWindowTop = Top;
                appSettings.SysInfoWindowHeight = Height;
                appSettings.SysInfoWindowWidth = Width;
                appSettings.Save();
            }
        }

        // ============================================================
        //  TEXTE FORMATÉ COMMUN (Copy + Export)
        // ============================================================
        private string GetCurrentClipboardFormatted()
        {
            StringBuilder sb = new StringBuilder();

            // ---------- SystemInfo ----------
            sb.AppendLine("SystemInfo");
            sb.AppendLine(new string('-', 50));

            if (TestGrid.ItemsSource is IEnumerable<object> sysInfoEnum)
            {
                var sysInfoList = sysInfoEnum.ToList();
                if (sysInfoList.Count > 0)
                {
                    int maxName = sysInfoList
                        .Max(i => i.GetType().GetProperty("Name")?
                                     .GetValue(i)?.ToString()?.Length ?? 0);

                    foreach (var item in sysInfoList)
                    {
                        var name = item.GetType().GetProperty("Name")?.GetValue(item, null);
                        var value = item.GetType().GetProperty("Value")?.GetValue(item, null);
                        sb.AppendLine($"{name?.ToString().PadRight(maxName)} : {value}");
                    }
                }
            }
            sb.AppendLine();

            // ---------- Memory Timings ----------
            sb.AppendLine("Memory Timings");
            sb.AppendLine(new string('-', 50));

            if (MemCfgGrid.ItemsSource is IEnumerable<dynamic> memCfgEnum)
            {
                var memList = memCfgEnum.Cast<dynamic>().ToList();
                if (memList.Count > 0)
                {
                    dynamic first = memList[0];
                    string[] values = first.Values as string[];
                    string header = "Name".PadRight(18);

                    if (values != null)
                    {
                        for (int i = 0; i < values.Length; i++)
                            header += $" | DCT {i}";
                    }

                    sb.AppendLine(header);
                    sb.AppendLine(new string('-', header.Length));

                    foreach (var row in memList)
                    {
                        string line = row.PropertyName.ToString().PadRight(18);
                        string[] vals = row.Values as string[];

                        if (vals != null)
                        {
                            foreach (var v in vals)
                                line += $" | {v}";
                        }

                        sb.AppendLine(line);
                    }
                }
            }
            sb.AppendLine();

            // ---------- AOD Table ----------
            sb.AppendLine("AOD Table");
            sb.AppendLine(new string('-', 50));

            if (MemControllerGrid.ItemsSource is IEnumerable<object> memCtrlEnum)
            {
                var memCtrlList = memCtrlEnum.ToList();
                if (memCtrlList.Count > 0)
                {
                    int maxName = memCtrlList
                        .Max(i => i.GetType().GetProperty("Name")?
                                     .GetValue(i)?.ToString()?.Length ?? 0);

                    foreach (var item in memCtrlList)
                    {
                        var name = item.GetType().GetProperty("Name")?.GetValue(item, null);
                        var value = item.GetType().GetProperty("Value")?.GetValue(item, null);
                        sb.AppendLine($"{name?.ToString().PadRight(maxName)} : {value}");
                    }
                }
            }

            return sb.ToString();
        }

        // ============================================================
        //  BOUTON COPY ALL
        // ============================================================
        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string text = GetCurrentClipboardFormatted();
                Clipboard.SetText(text);
            }
            catch
            {
                // On évite de faire planter l'appli pour un paste raté
            }
        }

        // ============================================================
        //  BOUTON EXPORT TXT
        // ============================================================
        private void ExportTxt_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Récupération mémoire / fréquence / timings primaires
                MemoryConfig memConfigs = CpuSingleton.Instance.GetMemoryConfig();
                int freq = memConfigs.DRAMFreq;

                var firstTiming = memConfigs.Timings.First().Value;
                Type tType = firstTiming.GetType();

                object clObj = tType.GetProperty("CL")?.GetValue(firstTiming);
                object rcdObj = tType.GetProperty("RCDRD")?.GetValue(firstTiming);
                object rpObj = tType.GetProperty("RP")?.GetValue(firstTiming);
                object rasObj = tType.GetProperty("RAS")?.GetValue(firstTiming);

                string cl = clObj?.ToString() ?? "?";
                string rcd = rcdObj?.ToString() ?? "?";
                string rp = rpObj?.ToString() ?? "?";
                string ras = rasObj?.ToString() ?? "?";

                string primaryTiming = $"CL{cl}-{rcd}-{rp}-{ras}";

                // Date/heure pour contenu
                string dateDisplay = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                // Nom de fichier : date_heure_freq_timing.txt
                string dateFilePart = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                string safeTimingPart = primaryTiming.Replace(" ", string.Empty)
                                                     .Replace("?", "X");
                string fileName = $"{dateFilePart}_{freq}MTs_{safeTimingPart}.txt";

                // Contenu
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Export ZenTimings - {dateDisplay}");
                sb.AppendLine($"Frequency : {freq} MT/s");
                sb.AppendLine($"Primary Timings : {primaryTiming}");
                sb.AppendLine();
                sb.Append(GetCurrentClipboardFormatted());

                // Boîte de dialogue de sauvegarde
                SaveFileDialog dlg = new SaveFileDialog
                {
                    FileName = fileName,
                    Filter = "Text File|*.txt"
                };

                if (dlg.ShowDialog() == true)
                {
                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error exporting TXT: " + ex.Message, "Export TXT",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
