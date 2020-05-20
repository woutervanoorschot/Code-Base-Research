﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using NotifyIcon = System.Windows.Forms.NotifyIcon;

/*
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
*/

namespace CodeBase
{
    public partial class MainWindow : Window
    {
        public DataManager<ApplicationData> DataManager = new DataManager<ApplicationData>("ApplicationData.json");
        public ApplicationData Data;

        private readonly ObservableCollection<Project> Projects;

        public readonly Heart Heart;
        private readonly Autorun Autorun;
        private readonly Inspector Inspector;

        private AddProjectWindow AddProjectWindow;
        private ServerAccessWindow ServerAccessWindow;

        private NotifyIcon NotifyIcon;

        public MainWindow()
        {
            InitializeComponent();
            CheckRunningOnce();
            //
            Load();
            //
            Heart = new Heart(Data, () => StartInspector());
            Autorun = new Autorun();
            Inspector = new Inspector();
            //
            Projects = new ObservableCollection<Project>(Data.Projects);
            UpdateProjectsList();
            //
            CreateTrayIcon();
            BindWindowEvents();
            Activate();
        }

        ~MainWindow() 
        {
            NotifyIcon.Dispose();
        }

        #region Other Methods

        void BindWindowEvents()
        {
            AutorunCheckBox.IsChecked = Autorun.GetState();

            // Поведение окна
            KeyDown += (sender, e) => { if (e.Key == Key.Tab) SendData(); };

            Closing += (sender, e) => {
                Hide();
                ShowInTaskbar = false;
                NotifyIcon.Visible = true;
                e.Cancel = true;
            };

            StateChanged += (sender, e) => {
                if (WindowState == WindowState.Minimized)
                {
                    NotifyIcon.Visible = true;
                    ShowInTaskbar = false;
                }
                else
                if (WindowState == WindowState.Normal)
                {
                    NotifyIcon.Visible = false;
                    ShowInTaskbar = true;
                }
            };
        }

        void CheckRunningOnce() 
        {
            var processes = Process.GetProcesses();
            var currentProcess = Process.GetCurrentProcess();
            foreach (var process in processes)
            {
                if (process.ProcessName == currentProcess.ProcessName)
                {
                    if (process.Id != currentProcess.Id)
                    {
                        MessageHelper.Alert($"{process.ProcessName} is already running! Close all instances and try again", "Error");
                        Close();
                        return;
                    }
                }
            }
        }

        void CreateTrayIcon() 
        {
            // Поведение иконки в tray
            NotifyIcon = new NotifyIcon();
            var Hicon = Properties.Resources.CodeBaseLogo.GetHicon();
            NotifyIcon.Icon = System.Drawing.Icon.FromHandle(Hicon);
            NotifyIcon.MouseClick += (sender, e) => {
                WindowState = WindowState.Normal;
                Activate(); // brings this window to forward
            };
        }

        #endregion

        #region Load & Save methods

        public void Load()
        {
            Data = DataManager.Load(ex => {
                MessageHelper.ThrowException(ex);
                Close();
            });
            //
            WebMethods.ReceiverURL = Data.ReceiverURL ?? "";
            
            if (Data.WindowWidth > 0)
                Width = Data.WindowWidth;
            if (Data.WindowHeight > 0)
                Height = Data.WindowHeight;

            WindowState = Data.WindowState;
        }

        public void Save()
        {
            Data.ReceiverURL = WebMethods.ReceiverURL;
            Data.Projects = Projects.ToArray();

            Data.WindowWidth = Width;
            Data.WindowHeight = Height;
            Data.WindowState = WindowState;
            //
            DataManager.Save(Data, MessageHelper.ThrowException);
        }

        private void UpdateProjectsList()
        {
            var list = new ObservableCollection<Project>(Projects.OrderBy(proj => -proj.LastEdit));
            Projects.Clear();
            foreach (var proj in list) 
            {
                Projects.Add(proj);
            }

            if (listBox.ItemsSource == null) 
            {
                listBox.Items.Clear();
                listBox.ItemsSource = Projects;
            }
            listBox.Items.Refresh();
            listBox.UpdateLayout();
        }

        public void StartInspector()
        {
            Inspector.Start(new List<Project>(Projects), (stage, info) => {
                Dispatcher.Invoke(() => {
                    switch (stage)
                    {
                        case InspectorStage.Progress:
                            ProgressBar.Visibility = Visibility.Visible;
                            (int progress, int total) = ((int, int))info;
                            ProgressBar.Maximum = total;
                            ProgressBar.Value = progress;
                            break;
                        case InspectorStage.Progress2:
                            ProgressBar2.Visibility = Visibility.Visible;
                            (progress, total) = ((int, int))info;
                            ProgressBar2.Maximum = total;
                            ProgressBar2.Value = progress;
                            break;
                        case InspectorStage.FetchingFiles:
                            StatusText.Content = $"Fetching files: {info}";
                            break;
                        case InspectorStage.FetchingLines:
                            StatusText.Content = $"Fetching lines: {info}";
                            break;
                        case InspectorStage.Completed:
                            StatusText.Content = "Complete";
                            UpdateProjectsList();
                            Save();
                            SendData();
                            //
                            ProgressBar.Visibility = Visibility.Hidden;
                            ProgressBar2.Visibility = Visibility.Hidden;
                            break;
                    }
                });
            });
        }

        public void SendData()
        {
            if (Data.SendData)
            {
                WebMethods.UpdateProjects(Data, Projects.Select(proj => (ProjectEntity)proj).ToArray(), result =>
                {
                    WebClient.ProcessResponse(result, data =>
                    {
                        if (data != "1")
                            MessageBox.Show(data);
                    });
                });
            }
        }

        #endregion

        #region Window Events

        private void AutorunCheckBox_Click(object sender, RoutedEventArgs e)
        {
            CheckBox ch = (CheckBox)sender;
            ch.IsChecked = Autorun.SetState(ch.IsChecked.Value, ex => {
                MessageHelper.Error(ex.ToString(), ex.GetType().Name);
            });
        }

        private void AddProjectButton_Click(object sender, RoutedEventArgs e)
        {
            if (AddProjectWindow != null)
            {
                AddProjectWindow.Close();
                AddProjectWindow = null;
            }

            AddProjectWindow = new AddProjectWindow((project) =>
            {
                Projects.Insert(0, project);
                AddProjectWindow.Close();
                //
                UpdateProjectsList();
                //
                Save();
            })
            {
                Owner = this
            };

            AddProjectWindow.Show();
        }

        private void ServerAccessButton_Click(object sender, RoutedEventArgs e)
        {
            if (ServerAccessWindow != null)
            {
                ServerAccessWindow.Close();
                ServerAccessWindow = null;
            }

            ServerAccessWindow = new ServerAccessWindow(Data, () => Save()) { Owner = this };
            ServerAccessWindow.Show();
        }

        private void ProjectOpenButton_Click(object sender, RoutedEventArgs e)
        {
            string title = (string)((Button)sender).Tag;

            foreach (var proj in Projects)
            {
                if (proj.Title == title)
                {
                    ProjectWindow win = new ProjectWindow(proj) { Owner = this };
                    win.Show();
                }
            }
        }

        private void ProjectDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            string title = (string)((Button)sender).Tag;

            foreach (var proj in Projects)
            {
                if (proj.Title == title)
                {
                    DeleteProjectDialog dialog = new DeleteProjectDialog(proj, () => {
                        Projects.Remove(proj);
                        //
                        UpdateProjectsList();
                        //
                        Save();
                    });
                    dialog.Show();
                }
            }
        }

        private void ProjectEditButton_Click(object sender, RoutedEventArgs e)
        {
            string title = (string)((Button)sender).Tag;

            foreach (var proj in Projects)
            {
                if (proj.Title == title)
                {
                    EditProjectWindow win = new EditProjectWindow(proj, () => {
                        UpdateProjectsList();
                        //
                        Save();
                    });
                    win.Show();
                }
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            StartInspector();
        }

        private void SummaryButton_Click(object sender, RoutedEventArgs e)
        {
            Project 
                All     = new Project("", "All projects")     { Info = new ProjectInfo() },
                Public  = new Project("", "Public projects")  { Info = new ProjectInfo() },
                Private = new Project("", "Private projects") { Info = new ProjectInfo() };

            foreach (var proj in Projects)
            {
                All.Info.Volume += proj.Info.Volume;
                merge(All.Info.ExtensionsVolume, proj.Info.ExtensionsVolume);

                if (proj.IsPublic)
                {
                    Public.Info.Volume += proj.Info.Volume;
                    merge(Public.Info.ExtensionsVolume, proj.Info.ExtensionsVolume);
                }
                else
                {
                    Private.Info.Volume += proj.Info.Volume;
                    merge(Private.Info.ExtensionsVolume, proj.Info.ExtensionsVolume);
                }

                All.Info.Errors.AddRange(proj.Info.Errors.Select(t => $"{proj.Title} -> {t}"));
                //
                void merge(Dictionary<string, CodeVolume> A, Dictionary<string, CodeVolume> B)
                {
                    foreach (var pair in B)
                    {
                        if (!A.ContainsKey(pair.Key))
                            A.Add(pair.Key, pair.Value);
                        else
                            A[pair.Key] += pair.Value;
                    }
                }

            }

            ProjectWindow win = new ProjectWindow(new Project[] { All, Public, Private })
            {
                Title = "Summary",
                Owner = this
            };
            win.Show();
        }

        #endregion
    }
}