﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class CommitDetail : ObservableObject
    {
        public DiffContext DiffContext
        {
            get => _diffContext;
            private set => SetProperty(ref _diffContext, value);
        }

        public int ActivePageIndex
        {
            get => _activePageIndex;
            set => SetProperty(ref _activePageIndex, value);
        }

        public Models.Commit Commit
        {
            get => _commit;
            set
            {
                if (SetProperty(ref _commit, value))
                    Refresh();
            }
        }

        public List<Models.Change> Changes
        {
            get => _changes;
            set => SetProperty(ref _changes, value);
        }

        public List<Models.Change> VisibleChanges
        {
            get => _visibleChanges;
            set => SetProperty(ref _visibleChanges, value);
        }

        public List<Models.Change> SelectedChanges
        {
            get => _selectedChanges;
            set
            {
                if (SetProperty(ref _selectedChanges, value))
                {
                    if (value == null || value.Count != 1)
                        DiffContext = null;
                    else
                        DiffContext = new DiffContext(_repo, new Models.DiffOption(_commit, value[0]), _diffContext);
                }
            }
        }

        public string SearchChangeFilter
        {
            get => _searchChangeFilter;
            set
            {
                if (SetProperty(ref _searchChangeFilter, value))
                {
                    RefreshVisibleChanges();
                }
            }
        }

        public HierarchicalTreeDataGridSource<FileTreeNode> RevisionFiles
        {
            get => _revisionFiles;
            private set => SetProperty(ref _revisionFiles, value);
        }

        public string SearchFileFilter
        {
            get => _searchFileFilter;
            set
            {
                if (SetProperty(ref _searchFileFilter, value))
                {
                    RefreshVisibleFiles();
                }
            }
        }

        public object ViewRevisionFileContent
        {
            get => _viewRevisionFileContent;
            set => SetProperty(ref _viewRevisionFileContent, value);
        }

        public CommitDetail(string repo)
        {
            _repo = repo;
        }

        public void Cleanup()
        {
            _repo = null;
            _commit = null;
            if (_changes != null)
                _changes.Clear();
            if (_visibleChanges != null)
                _visibleChanges.Clear();
            if (_selectedChanges != null)
                _selectedChanges.Clear();
            _searchChangeFilter = null;
            _diffContext = null;
            if (_revisionFilesBackup != null)
                _revisionFilesBackup.Clear();
            if (_revisionFiles != null)
                _revisionFiles.Dispose();
            _searchFileFilter = null;
            _viewRevisionFileContent = null;
            _cancelToken = null;
        }

        public void NavigateTo(string commitSHA)
        {
            var repo = Preference.FindRepository(_repo);
            if (repo != null)
                repo.NavigateToCommit(commitSHA);
        }

        public void ClearSearchChangeFilter()
        {
            SearchChangeFilter = string.Empty;
        }

        public void ClearSearchFileFilter()
        {
            SearchFileFilter = string.Empty;
        }

        public ContextMenu CreateChangeContextMenu(Models.Change change)
        {
            var menu = new ContextMenu();

            var diffWithMerger = new MenuItem();
            diffWithMerger.Header = App.Text("DiffWithMerger");
            diffWithMerger.Icon = App.CreateMenuIcon("Icons.Diff");
            diffWithMerger.Click += (_, ev) =>
            {
                var opt = new Models.DiffOption(_commit, change);
                var type = Preference.Instance.ExternalMergeToolType;
                var exec = Preference.Instance.ExternalMergeToolPath;

                var tool = Models.ExternalMerger.Supported.Find(x => x.Type == type);
                if (tool == null || !File.Exists(exec))
                {
                    App.RaiseException(_repo, "Invalid merge tool in preference setting!");
                    return;
                }

                var args = tool.Type != 0 ? tool.DiffCmd : Preference.Instance.ExternalMergeToolDiffCmd;
                Task.Run(() => Commands.MergeTool.OpenForDiff(_repo, exec, args, opt));
                ev.Handled = true;
            };
            menu.Items.Add(diffWithMerger);

            if (change.Index != Models.ChangeState.Deleted)
            {
                var history = new MenuItem();
                history.Header = App.Text("FileHistory");
                history.Icon = App.CreateMenuIcon("Icons.Histories");
                history.Click += (_, ev) =>
                {
                    var window = new Views.FileHistories() { DataContext = new FileHistories(_repo, change.Path) };
                    window.Show();
                    ev.Handled = true;
                };

                var blame = new MenuItem();
                blame.Header = App.Text("Blame");
                blame.Icon = App.CreateMenuIcon("Icons.Blame");
                blame.Click += (o, ev) =>
                {
                    var window = new Views.Blame() { DataContext = new Blame(_repo, change.Path, _commit.SHA) };
                    window.Show();
                    ev.Handled = true;
                };

                var full = Path.GetFullPath(Path.Combine(_repo, change.Path));
                var explore = new MenuItem();
                explore.Header = App.Text("RevealFile");
                explore.Icon = App.CreateMenuIcon("Icons.Folder.Open");
                explore.IsEnabled = File.Exists(full);
                explore.Click += (_, ev) =>
                {
                    Native.OS.OpenInFileManager(full, true);
                    ev.Handled = true;
                };

                menu.Items.Add(new MenuItem { Header = "-" });
                menu.Items.Add(history);
                menu.Items.Add(blame);
                menu.Items.Add(explore);
                menu.Items.Add(new MenuItem { Header = "-" });
            }

            var copyPath = new MenuItem();
            copyPath.Header = App.Text("CopyPath");
            copyPath.Icon = App.CreateMenuIcon("Icons.Copy");
            copyPath.Click += (_, ev) =>
            {
                App.CopyText(change.Path);
                ev.Handled = true;
            };
            menu.Items.Add(copyPath);

            return menu;
        }

        public ContextMenu CreateRevisionFileContextMenu(Models.Object file)
        {
            var history = new MenuItem();
            history.Header = App.Text("FileHistory");
            history.Icon = App.CreateMenuIcon("Icons.Histories");
            history.Click += (_, ev) =>
            {
                var window = new Views.FileHistories() { DataContext = new FileHistories(_repo, file.Path) };
                window.Show();
                ev.Handled = true;
            };

            var blame = new MenuItem();
            blame.Header = App.Text("Blame");
            blame.Icon = App.CreateMenuIcon("Icons.Blame");
            blame.Click += (o, ev) =>
            {
                var window = new Views.Blame() { DataContext = new Blame(_repo, file.Path, _commit.SHA) };
                window.Show();
                ev.Handled = true;
            };

            var full = Path.GetFullPath(Path.Combine(_repo, file.Path));
            var explore = new MenuItem();
            explore.Header = App.Text("RevealFile");
            explore.Icon = App.CreateMenuIcon("Icons.Folder.Open");
            explore.Click += (_, ev) =>
            {
                Native.OS.OpenInFileManager(full, file.Type == Models.ObjectType.Blob);
                ev.Handled = true;
            };

            var saveAs = new MenuItem();
            saveAs.Header = App.Text("SaveAs");
            saveAs.Icon = App.CreateMenuIcon("Icons.Save");
            saveAs.IsEnabled = file.Type == Models.ObjectType.Blob;
            saveAs.Click += async (_, ev) =>
            {
                var topLevel = App.GetTopLevel();
                if (topLevel == null)
                    return;

                var options = new FolderPickerOpenOptions() { AllowMultiple = false };
                var selected = await topLevel.StorageProvider.OpenFolderPickerAsync(options);
                if (selected.Count == 1)
                {
                    var saveTo = Path.Combine(selected[0].Path.LocalPath, Path.GetFileName(file.Path));
                    Commands.SaveRevisionFile.Run(_repo, _commit.SHA, file.Path, saveTo);
                }

                ev.Handled = true;
            };

            var copyPath = new MenuItem();
            copyPath.Header = App.Text("CopyPath");
            copyPath.Icon = App.CreateMenuIcon("Icons.Copy");
            copyPath.Click += (_, ev) =>
            {
                App.CopyText(file.Path);
                ev.Handled = true;
            };

            var menu = new ContextMenu();
            menu.Items.Add(history);
            menu.Items.Add(blame);
            menu.Items.Add(explore);
            menu.Items.Add(saveAs);
            menu.Items.Add(copyPath);
            return menu;
        }

        private void Refresh()
        {
            _changes = null;
            VisibleChanges = null;
            SelectedChanges = null;

            if (_revisionFiles != null)
            {
                _revisionFiles.Dispose();
                _revisionFiles = null;
            }

            if (_commit == null)
                return;
            if (_cancelToken != null)
                _cancelToken.Requested = true;

            _cancelToken = new Commands.Command.CancelToken();

            var parent = _commit.Parents.Count == 0 ? "4b825dc642cb6eb9a060e54bf8d69288fbee4904" : _commit.Parents[0];
            var cmdChanges = new Commands.CompareRevisions(_repo, parent, _commit.SHA) { Cancel = _cancelToken };
            var cmdRevisionFiles = new Commands.QueryRevisionObjects(_repo, _commit.SHA) { Cancel = _cancelToken };

            Task.Run(() =>
            {
                var changes = cmdChanges.Result();
                if (cmdChanges.Cancel.Requested)
                    return;

                var visible = changes;
                if (!string.IsNullOrWhiteSpace(_searchChangeFilter))
                {
                    visible = new List<Models.Change>();
                    foreach (var c in changes)
                    {
                        if (c.Path.Contains(_searchChangeFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            visible.Add(c);
                        }
                    }
                }

                var tree = FileTreeNode.Build(visible, true);
                Dispatcher.UIThread.Invoke(() =>
                {
                    Changes = changes;
                    VisibleChanges = visible;
                });
            });

            Task.Run(() =>
            {
                _revisionFilesBackup = cmdRevisionFiles.Result();
                if (cmdRevisionFiles.Cancel.Requested)
                    return;

                var visible = _revisionFilesBackup;
                var isSearching = !string.IsNullOrWhiteSpace(_searchFileFilter);
                if (isSearching)
                {
                    visible = new List<Models.Object>();
                    foreach (var f in _revisionFilesBackup)
                    {
                        if (f.Path.Contains(_searchFileFilter, StringComparison.OrdinalIgnoreCase))
                            visible.Add(f);
                    }
                }

                var tree = FileTreeNode.Build(visible, isSearching || visible.Count <= 100);
                Dispatcher.UIThread.Invoke(() => BuildRevisionFilesSource(tree));
            });
        }

        private void RefreshVisibleChanges()
        {
            if (_changes == null)
                return;

            if (string.IsNullOrEmpty(_searchChangeFilter))
            {
                VisibleChanges = _changes;
            }
            else
            {
                var visible = new List<Models.Change>();
                foreach (var c in _changes)
                {
                    if (c.Path.Contains(_searchChangeFilter, StringComparison.OrdinalIgnoreCase))
                        visible.Add(c);
                }

                VisibleChanges = visible;
            }
        }

        private void RefreshVisibleFiles()
        {
            if (_revisionFiles == null)
                return;

            var visible = _revisionFilesBackup;
            var isSearching = !string.IsNullOrWhiteSpace(_searchFileFilter);
            if (isSearching)
            {
                visible = new List<Models.Object>();
                foreach (var f in _revisionFilesBackup)
                {
                    if (f.Path.Contains(_searchFileFilter, StringComparison.OrdinalIgnoreCase))
                        visible.Add(f);
                }
            }

            BuildRevisionFilesSource(FileTreeNode.Build(visible, isSearching || visible.Count < 100));
        }

        private void RefreshViewRevisionFile(Models.Object file)
        {
            if (file == null)
            {
                ViewRevisionFileContent = null;
                return;
            }

            switch (file.Type)
            {
                case Models.ObjectType.Blob:
                    Task.Run(() =>
                    {
                        var isBinary = new Commands.IsBinary(_repo, _commit.SHA, file.Path).Result();
                        if (isBinary)
                        {
                            var ext = Path.GetExtension(file.Path);
                            if (IMG_EXTS.Contains(ext))
                            {
                                var stream = Commands.QueryFileContent.Run(_repo, _commit.SHA, file.Path);
                                var bitmap = stream.Length > 0 ? new Bitmap(stream) : null;
                                Dispatcher.UIThread.Invoke(() =>
                                {
                                    ViewRevisionFileContent = new Models.RevisionImageFile() { Image = bitmap };
                                });
                            }
                            else
                            {
                                var size = new Commands.QueryFileSize(_repo, file.Path, _commit.SHA).Result();
                                Dispatcher.UIThread.Invoke(() =>
                                {
                                    ViewRevisionFileContent = new Models.RevisionBinaryFile() { Size = size };
                                });
                            }

                            return;
                        }

                        var contentStream = Commands.QueryFileContent.Run(_repo, _commit.SHA, file.Path);
                        var content = new StreamReader(contentStream).ReadToEnd();
                        if (content.StartsWith("version https://git-lfs.github.com/spec/", StringComparison.Ordinal))
                        {
                            var obj = new Models.RevisionLFSObject() { Object = new Models.LFSObject() };
                            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                            if (lines.Length == 3)
                            {
                                foreach (var line in lines)
                                {
                                    if (line.StartsWith("oid sha256:", StringComparison.Ordinal))
                                    {
                                        obj.Object.Oid = line.Substring(11);
                                    }
                                    else if (line.StartsWith("size ", StringComparison.Ordinal))
                                    {
                                        obj.Object.Size = long.Parse(line.Substring(5));
                                    }
                                }
                                Dispatcher.UIThread.Invoke(() =>
                                {
                                    ViewRevisionFileContent = obj;
                                });
                                return;
                            }
                        }

                        Dispatcher.UIThread.Invoke(() =>
                        {
                            ViewRevisionFileContent = new Models.RevisionTextFile()
                            {
                                FileName = file.Path,
                                Content = content
                            };
                        });
                    });
                    break;
                case Models.ObjectType.Commit:
                    ViewRevisionFileContent = new Models.RevisionSubmodule() { SHA = file.SHA };
                    break;
                default:
                    ViewRevisionFileContent = null;
                    break;
            }
        }

        private void BuildRevisionFilesSource(List<FileTreeNode> tree)
        {
            var source = new HierarchicalTreeDataGridSource<FileTreeNode>(tree)
            {
                Columns =
                {
                    new HierarchicalExpanderColumn<FileTreeNode>(
                        new TemplateColumn<FileTreeNode>("Icon", "FileTreeNodeExpanderTemplate", null, GridLength.Auto),
                        x => x.Children,
                        x => x.Children.Count > 0,
                        x => x.IsExpanded),
                    new TextColumn<FileTreeNode, string>(
                        null,
                        x => string.Empty,
                        GridLength.Star)
                }
            };

            var selection = new Models.TreeDataGridSelectionModel<FileTreeNode>(source, x => x.Children);
            selection.SingleSelect = true;
            selection.RowDoubleTapped += (s, e) =>
            {
                var model = s as Models.TreeDataGridSelectionModel<FileTreeNode>;
                var node = model.SelectedItem;
                if (node != null && node.IsFolder)
                    node.IsExpanded = !node.IsExpanded;
            };
            selection.SelectionChanged += (s, _) =>
            {
                if (s is Models.TreeDataGridSelectionModel<FileTreeNode> selection)
                    RefreshViewRevisionFile(selection.SelectedItem?.Backend as Models.Object);
            };

            source.Selection = selection;
            RevisionFiles = source;
        }

        private static readonly HashSet<string> IMG_EXTS = new HashSet<string>()
        {
            ".ico", ".bmp", ".jpg", ".png", ".jpeg"
        };

        private string _repo = string.Empty;
        private int _activePageIndex = 0;
        private Models.Commit _commit = null;
        private List<Models.Change> _changes = null;
        private List<Models.Change> _visibleChanges = null;
        private List<Models.Change> _selectedChanges = null;
        private string _searchChangeFilter = string.Empty;
        private DiffContext _diffContext = null;
        private List<Models.Object> _revisionFilesBackup = null;
        private HierarchicalTreeDataGridSource<FileTreeNode> _revisionFiles = null;
        private string _searchFileFilter = string.Empty;
        private object _viewRevisionFileContent = null;
        private Commands.Command.CancelToken _cancelToken = null;
    }
}
