global using CSharpRPGBackend.Core;
global using CSharpRPGBackend.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace RPGGameEditor
{
    public partial class GameEditorForm : Form
    {
        private GameDefinition _currentGame;
        private string _currentGamePath = "";
        private readonly string _gamesDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games");

        // Game content loaded from directories
        private List<RoomDefinition> _rooms = new();
        private List<NpcDefinition> _npcs = new();
        private List<ItemDefinition> _items = new();
        private List<QuestDefinition> _quests = new();

        // UI controls we need to access
        private TreeView _treeView;

        public GameEditorForm()
        {
            InitializeComponent();
            InitializeUI();
            this.Load += OnFormLoad;
        }

        private void InitializeUI()
        {
            // Form properties
            this.Text = "RPG Game Editor";
            this.ClientSize = new System.Drawing.Size(1200, 800);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.MinimumSize = new System.Drawing.Size(800, 600);

            // Create MenuStrip - MUST set Dock.Top explicitly
            var menuStrip = new MenuStrip
            {
                Dock = DockStyle.Top
            };

            // File menu
            var fileMenu = new ToolStripMenuItem("&File");
            fileMenu.DropDownItems.Add("&New Game", null, NewGame_Click);
            fileMenu.DropDownItems.Add("&Open Game", null, OpenGame_Click);
            fileMenu.DropDownItems.Add("&Save", null, SaveGame_Click);
            fileMenu.DropDownItems.Add("&Save As", null, SaveGameAs_Click);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("E&xit", null, (s, e) => Close());
            menuStrip.Items.Add(fileMenu);

            // Edit menu
            var editMenu = new ToolStripMenuItem("&Edit");
            editMenu.DropDownItems.Add("&Game Properties", null, EditGameProperties_Click);
            menuStrip.Items.Add(editMenu);

            // Help menu
            var helpMenu = new ToolStripMenuItem("&Help");
            helpMenu.DropDownItems.Add("&About", null, ShowAbout);
            menuStrip.Items.Add(helpMenu);

            // TreeView for game structure (full width)
            _treeView = new TreeView
            {
                Dock = DockStyle.Fill
            };

            // Add context menu to TreeView
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Add Room", null, (s, e) => AddRoom());
            contextMenu.Items.Add("Add NPC", null, (s, e) => AddNPC());
            contextMenu.Items.Add("Add Item", null, (s, e) => AddItem());
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add("Edit", null, (s, e) => EditSelected());
            contextMenu.Items.Add("Delete", null, (s, e) => DeleteSelected());
            _treeView.ContextMenuStrip = contextMenu;
            _treeView.DoubleClick += (s, e) => EditSelected();

            // Add controls to form in correct order
            this.Controls.Add(_treeView);
            this.Controls.Add(menuStrip);
        }

        private void ShowAbout(object? sender, EventArgs e)
        {
            MessageBox.Show("RPG Game Editor v1.0\n\nCreate and edit JSON-based RPG games.",
                "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnFormLoad(object? sender, EventArgs e)
        {
            LoadGameList();
        }

        private void LoadGameList()
        {
            if (!Directory.Exists(_gamesDirectory))
                Directory.CreateDirectory(_gamesDirectory);

            var gameDirs = Directory.GetDirectories(_gamesDirectory);
            // Load available games - TODO: Implement game listing
        }

        private void NewGame_Click(object sender, EventArgs e)
        {
            var dialog = new NewGameDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _currentGame = dialog.CreatedGame;
                _currentGamePath = dialog.GamePath;
                MessageBox.Show("New game created successfully!");
                UpdateUI();
            }
        }

        private void OpenGame_Click(object sender, EventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Game JSON (game.json)|game.json",
                InitialDirectory = _gamesDirectory
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    var json = File.ReadAllText(dialog.FileName);
                    _currentGame = JsonSerializer.Deserialize<GameDefinition>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    _currentGamePath = Path.GetDirectoryName(dialog.FileName);

                    // Load rooms, NPCs, items, quests from subdirectories
                    LoadGameContent();

                    MessageBox.Show("Game loaded successfully!");
                    UpdateUI();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading game: {ex.Message}");
                }
            }
        }

        private void LoadGameContent()
        {
            _rooms.Clear();
            _npcs.Clear();
            _items.Clear();
            _quests.Clear();

            if (string.IsNullOrEmpty(_currentGamePath))
                return;

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            // Load rooms
            var roomsPath = Path.Combine(_currentGamePath, "rooms");
            if (Directory.Exists(roomsPath))
            {
                foreach (var file in Directory.GetFiles(roomsPath, "*.json"))
                {
                    try
                    {
                        var roomJson = File.ReadAllText(file);
                        var room = JsonSerializer.Deserialize<RoomDefinition>(roomJson, options);
                        if (room != null)
                            _rooms.Add(room);
                    }
                    catch { /* Skip invalid files */ }
                }
            }

            // Load NPCs
            var npcsPath = Path.Combine(_currentGamePath, "npcs");
            if (Directory.Exists(npcsPath))
            {
                foreach (var file in Directory.GetFiles(npcsPath, "*.json"))
                {
                    try
                    {
                        var npcJson = File.ReadAllText(file);
                        var npc = JsonSerializer.Deserialize<NpcDefinition>(npcJson, options);
                        if (npc != null)
                            _npcs.Add(npc);
                    }
                    catch { /* Skip invalid files */ }
                }
            }

            // Load items
            var itemsPath = Path.Combine(_currentGamePath, "items");
            if (Directory.Exists(itemsPath))
            {
                foreach (var file in Directory.GetFiles(itemsPath, "*.json"))
                {
                    try
                    {
                        var itemJson = File.ReadAllText(file);
                        var item = JsonSerializer.Deserialize<ItemDefinition>(itemJson, options);
                        if (item != null)
                            _items.Add(item);
                    }
                    catch { /* Skip invalid files */ }
                }
            }

            // Load quests
            var questsPath = Path.Combine(_currentGamePath, "quests");
            if (Directory.Exists(questsPath))
            {
                foreach (var file in Directory.GetFiles(questsPath, "*.json"))
                {
                    try
                    {
                        var questJson = File.ReadAllText(file);
                        var quest = JsonSerializer.Deserialize<QuestDefinition>(questJson, options);
                        if (quest != null)
                            _quests.Add(quest);
                    }
                    catch { /* Skip invalid files */ }
                }
            }
        }

        private void SaveGame_Click(object sender, EventArgs e)
        {
            if (_currentGame == null)
            {
                MessageBox.Show("No game loaded or created.");
                return;
            }

            if (string.IsNullOrEmpty(_currentGamePath))
            {
                SaveGameAs_Click(sender, e);
                return;
            }

            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };

                // Save game.json
                var gameJsonPath = Path.Combine(_currentGamePath, "game.json");
                var gameJson = JsonSerializer.Serialize(_currentGame, options);
                File.WriteAllText(gameJsonPath, gameJson);

                // Save rooms to rooms/ subdirectory
                var roomsPath = Path.Combine(_currentGamePath, "rooms");
                Directory.CreateDirectory(roomsPath);
                foreach (var room in _rooms)
                {
                    var roomFilePath = Path.Combine(roomsPath, $"{room.Id}.json");
                    var roomJson = JsonSerializer.Serialize(room, options);
                    File.WriteAllText(roomFilePath, roomJson);
                }

                // Save NPCs to npcs/ subdirectory
                var npcsPath = Path.Combine(_currentGamePath, "npcs");
                Directory.CreateDirectory(npcsPath);
                foreach (var npc in _npcs)
                {
                    var npcFilePath = Path.Combine(npcsPath, $"{npc.Id}.json");
                    var npcJson = JsonSerializer.Serialize(npc, options);
                    File.WriteAllText(npcFilePath, npcJson);
                }

                // Save items to items/ subdirectory
                var itemsPath = Path.Combine(_currentGamePath, "items");
                Directory.CreateDirectory(itemsPath);
                foreach (var item in _items)
                {
                    var itemFilePath = Path.Combine(itemsPath, $"{item.Id}.json");
                    var itemJson = JsonSerializer.Serialize(item, options);
                    File.WriteAllText(itemFilePath, itemJson);
                }

                // Save quests to quests/ subdirectory
                var questsPath = Path.Combine(_currentGamePath, "quests");
                Directory.CreateDirectory(questsPath);
                foreach (var quest in _quests)
                {
                    var questFilePath = Path.Combine(questsPath, $"{quest.Id}.json");
                    var questJson = JsonSerializer.Serialize(quest, options);
                    File.WriteAllText(questFilePath, questJson);
                }

                MessageBox.Show($"Game saved successfully!\n\nSaved:\n- Game definition\n- {_rooms.Count} rooms\n- {_npcs.Count} NPCs\n- {_items.Count} items\n- {_quests.Count} quests");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving game: {ex.Message}");
            }
        }

        private void SaveGameAs_Click(object sender, EventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Game Directory|*.*",
                InitialDirectory = _gamesDirectory
            };

            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _currentGamePath = Path.GetDirectoryName(dialog.FileName);
                if (!Directory.Exists(_currentGamePath))
                    Directory.CreateDirectory(_currentGamePath);
                SaveGame_Click(sender, e);
            }
        }

        private void EditGameProperties_Click(object sender, EventArgs e)
        {
            if (_currentGame == null)
            {
                MessageBox.Show("No game loaded or created.");
                return;
            }

            var propsDialog = new GamePropertiesDialog(_currentGame, _items);
            if (propsDialog.ShowDialog() == DialogResult.OK)
            {
                MessageBox.Show("Game properties updated!");
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            this.Text = $"RPG Game Editor - {_currentGame?.Title ?? "No Game Loaded"}";

            if (_currentGame == null)
            {
                _treeView.Nodes.Clear();
                return;
            }

            // Populate TreeView with game structure
            _treeView.Nodes.Clear();

            // Game Info node
            var gameNode = _treeView.Nodes.Add("game", $"Game: {_currentGame.Title}");

            // Rooms node
            var roomsNode = gameNode.Nodes.Add("rooms", $"Rooms ({_rooms.Count})");
            foreach (var room in _rooms)
            {
                var roomNode = roomsNode.Nodes.Add($"room_{room.Id}", $"{room.Name} ({room.Id})");
                roomNode.Tag = room;
            }

            // NPCs node
            var npcsNode = gameNode.Nodes.Add("npcs", $"NPCs ({_npcs.Count})");
            foreach (var npc in _npcs)
            {
                var npcNode = npcsNode.Nodes.Add($"npc_{npc.Id}", $"{npc.Name} ({npc.Id})");
                npcNode.Tag = npc;
            }

            // Items node
            var itemsNode = gameNode.Nodes.Add("items", $"Items ({_items.Count})");
            foreach (var item in _items)
            {
                var itemNode = itemsNode.Nodes.Add($"item_{item.Id}", $"{item.Name} ({item.Id})");
                itemNode.Tag = item;
            }

            // Quests node
            var questsNode = gameNode.Nodes.Add("quests", $"Quests ({_quests.Count})");
            foreach (var quest in _quests)
            {
                var questNode = questsNode.Nodes.Add($"quest_{quest.Id}", $"{quest.Title} ({quest.Id})");
                questNode.Tag = quest;
            }

            gameNode.Expand();
        }

        private void AddRoom()
        {
            if (_currentGame == null)
            {
                MessageBox.Show("Please load or create a game first.");
                return;
            }

            var dialog = new RoomEditorDialog(null, _rooms);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _rooms.Add(dialog.CreatedRoom);
                UpdateUI();
            }
        }

        private void AddNPC()
        {
            if (_currentGame == null)
            {
                MessageBox.Show("Please load or create a game first.");
                return;
            }

            var dialog = new NpcEditorDialog(null, _rooms, _items);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _npcs.Add(dialog.CreatedNpc);
                UpdateUI();
            }
        }

        private void AddItem()
        {
            if (_currentGame == null)
            {
                MessageBox.Show("Please load or create a game first.");
                return;
            }

            var dialog = new ItemEditorDialog();
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _items.Add(dialog.CreatedItem);
                UpdateUI();
            }
        }

        private void EditSelected()
        {
            if (_treeView.SelectedNode == null || _treeView.SelectedNode.Tag == null)
                return; // Nothing selected or category node selected

            var tag = _treeView.SelectedNode.Tag;

            if (tag is RoomDefinition room)
            {
                var dialog = new RoomEditorDialog(room, _rooms);
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var index = _rooms.IndexOf(room);
                    if (index >= 0)
                        _rooms[index] = dialog.CreatedRoom;
                    UpdateUI();
                }
            }
            else if (tag is NpcDefinition npc)
            {
                var dialog = new NpcEditorDialog(npc, _rooms, _items);
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var index = _npcs.IndexOf(npc);
                    if (index >= 0)
                        _npcs[index] = dialog.CreatedNpc;
                    UpdateUI();
                }
            }
            else if (tag is ItemDefinition item)
            {
                var dialog = new ItemEditorDialog(item);
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    var index = _items.IndexOf(item);
                    if (index >= 0)
                        _items[index] = dialog.CreatedItem;
                    UpdateUI();
                }
            }
            else if (tag is QuestDefinition quest)
            {
                MessageBox.Show("Quest editing not yet implemented.");
            }
        }

        private void DeleteSelected()
        {
            if (_treeView.SelectedNode == null || _treeView.SelectedNode.Tag == null)
            {
                MessageBox.Show("Please select an item to delete.");
                return;
            }

            var result = MessageBox.Show("Are you sure you want to delete this item?",
                "Confirm Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (result == DialogResult.Yes)
            {
                var tag = _treeView.SelectedNode.Tag;
                if (tag is RoomDefinition room)
                    _rooms.Remove(room);
                else if (tag is NpcDefinition npc)
                    _npcs.Remove(npc);
                else if (tag is ItemDefinition item)
                    _items.Remove(item);
                else if (tag is QuestDefinition quest)
                    _quests.Remove(quest);

                UpdateUI();
            }
        }
    }

    public class NewGameDialog : Form
    {
        public GameDefinition CreatedGame { get; private set; }
        public string GamePath { get; private set; }

        private TextBox gameIdTextBox;
        private TextBox gameTitleTextBox;
        private TextBox gameSubtitleTextBox;

        public NewGameDialog()
        {
            InitializeDialog();
        }

        private void InitializeDialog()
        {
            this.Text = "Create New Game";
            this.Size = new System.Drawing.Size(400, 300);
            this.StartPosition = FormStartPosition.CenterParent;
            this.DialogResult = DialogResult.Cancel;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(10)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            // Game ID
            panel.Controls.Add(new Label { Text = "Game ID:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            gameIdTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(gameIdTextBox, 1, 0);

            // Game Title
            panel.Controls.Add(new Label { Text = "Title:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            gameTitleTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(gameTitleTextBox, 1, 1);

            // Game Subtitle
            panel.Controls.Add(new Label { Text = "Subtitle:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
            gameSubtitleTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(gameSubtitleTextBox, 1, 2);

            // Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var cancelBtn = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            var okBtn = new Button { Text = "Create", Width = 80, DialogResult = DialogResult.OK };
            okBtn.Click += (s, e) => CreateGame();

            buttonPanel.Controls.Add(cancelBtn);
            buttonPanel.Controls.Add(okBtn);

            this.Controls.Add(panel);
            this.Controls.Add(buttonPanel);
            this.CancelButton = cancelBtn;
            this.AcceptButton = okBtn;
        }

        private void CreateGame()
        {
            if (string.IsNullOrWhiteSpace(gameIdTextBox.Text) ||
                string.IsNullOrWhiteSpace(gameTitleTextBox.Text))
            {
                MessageBox.Show("Please fill in Game ID and Title.");
                this.DialogResult = DialogResult.None;
                return;
            }

            CreatedGame = new GameDefinition
            {
                Id = gameIdTextBox.Text,
                Title = gameTitleTextBox.Text,
                Subtitle = gameSubtitleTextBox.Text,
                Version = "1.0",
                Description = "",
                Metadata = new MetadataDefinition(),
                GameSettings = new GameSettingsDefinition
                {
                    StartingRoomId = "start",
                    PlayerStartingHealth = 100,
                    PlayerStartingLevel = 1
                }
            };

            var gamesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "games");
            GamePath = Path.Combine(gamesDir, gameIdTextBox.Text);
            Directory.CreateDirectory(GamePath);

            // Create subdirectories
            Directory.CreateDirectory(Path.Combine(GamePath, "rooms"));
            Directory.CreateDirectory(Path.Combine(GamePath, "npcs"));
            Directory.CreateDirectory(Path.Combine(GamePath, "items"));
            Directory.CreateDirectory(Path.Combine(GamePath, "quests"));

            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }

    public class GamePropertiesDialog : Form
    {
        private GameDefinition _game;
        private List<ItemDefinition> _allItems;

        public GamePropertiesDialog(GameDefinition game, List<ItemDefinition> allItems = null)
        {
            _game = game;
            _allItems = allItems ?? new List<ItemDefinition>();
            InitializeDialog();
        }

        private void InitializeDialog()
        {
            this.Text = "Game Properties";
            this.Size = new System.Drawing.Size(600, 750);
            this.StartPosition = FormStartPosition.CenterParent;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 12,
                Padding = new Padding(10),
                AutoScroll = true
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            // Title
            panel.Controls.Add(new Label { Text = "Title:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            var titleBox = new TextBox { Text = _game.Title, Dock = DockStyle.Fill };
            panel.Controls.Add(titleBox, 1, 0);

            // Subtitle
            panel.Controls.Add(new Label { Text = "Subtitle:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            var subtitleBox = new TextBox { Text = _game.Subtitle ?? "", Dock = DockStyle.Fill };
            panel.Controls.Add(subtitleBox, 1, 1);

            // Description
            panel.Controls.Add(new Label { Text = "Description:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 2);
            var descBox = new TextBox { Text = _game.Description ?? "", Multiline = true, Dock = DockStyle.Fill, Height = 60 };
            panel.Controls.Add(descBox, 1, 2);

            // Starting Room
            panel.Controls.Add(new Label { Text = "Starting Room:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 3);
            var startRoomBox = new TextBox { Text = _game.GameSettings?.StartingRoomId ?? "start", Dock = DockStyle.Fill };
            panel.Controls.Add(startRoomBox, 1, 3);

            // Starting Health
            panel.Controls.Add(new Label { Text = "Starting Health:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 4);
            var healthBox = new TextBox { Text = (_game.GameSettings?.PlayerStartingHealth ?? 100).ToString(), Dock = DockStyle.Fill };
            panel.Controls.Add(healthBox, 1, 4);

            // Player Name
            panel.Controls.Add(new Label { Text = "Player Name:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 5);
            var playerNameBox = new TextBox
            {
                Text = _game.Metadata?.Tags?.FirstOrDefault(t => t.StartsWith("player:"))?.Substring(7) ?? "Player",
                Dock = DockStyle.Fill
            };
            panel.Controls.Add(playerNameBox, 1, 5);

            // Style Theme
            panel.Controls.Add(new Label { Text = "Theme:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 6);
            var themeBox = new TextBox { Text = _game.Style?.Theme ?? "fantasy", Dock = DockStyle.Fill };
            panel.Controls.Add(themeBox, 1, 6);

            // Style Tonality
            panel.Controls.Add(new Label { Text = "Tonality:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 7);
            var tonalityBox = new TextBox { Text = _game.Style?.Tonality ?? "", Dock = DockStyle.Fill };
            panel.Controls.Add(tonalityBox, 1, 7);

            // Narrator Voice
            panel.Controls.Add(new Label { Text = "Narrator Voice:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 8);
            var narratorBox = new TextBox { Text = _game.Style?.NarratorVoice ?? "", Multiline = true, Dock = DockStyle.Fill, Height = 60 };
            panel.Controls.Add(narratorBox, 1, 8);

            // Starting Items
            panel.Controls.Add(new Label { Text = "Starting Items:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 9);
            var startingItemsListBox = new CheckedListBox { Dock = DockStyle.Fill, Height = 120 };
            foreach (var item in _allItems)
            {
                startingItemsListBox.Items.Add($"{item.Name} ({item.Id})");
                // Check if this item is already in starting items
                if (_game.StartingItems?.Any(si => si.ItemId == item.Id) == true)
                {
                    startingItemsListBox.SetItemChecked(startingItemsListBox.Items.Count - 1, true);
                }
            }
            panel.Controls.Add(startingItemsListBox, 1, 9);

            // Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var cancelBtn = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            var okBtn = new Button { Text = "OK", Width = 80 };
            okBtn.Click += (s, e) =>
            {
                _game.Title = titleBox.Text;
                _game.Subtitle = subtitleBox.Text;
                _game.Description = descBox.Text;

                // Game Settings
                if (_game.GameSettings == null)
                    _game.GameSettings = new GameSettingsDefinition();
                _game.GameSettings.StartingRoomId = startRoomBox.Text;
                if (int.TryParse(healthBox.Text, out int health))
                    _game.GameSettings.PlayerStartingHealth = health;

                // Player Name (stored in metadata tags)
                if (_game.Metadata == null)
                    _game.Metadata = new MetadataDefinition();
                _game.Metadata.Tags.RemoveAll(t => t.StartsWith("player:"));
                if (!string.IsNullOrWhiteSpace(playerNameBox.Text))
                    _game.Metadata.Tags.Add($"player:{playerNameBox.Text}");

                // Style Settings
                if (_game.Style == null)
                    _game.Style = new StyleSettingsDefinition();
                _game.Style.Theme = themeBox.Text;
                _game.Style.Tonality = tonalityBox.Text;
                _game.Style.NarratorVoice = narratorBox.Text;

                // Starting Items
                _game.StartingItems.Clear();
                for (int i = 0; i < startingItemsListBox.CheckedItems.Count; i++)
                {
                    var checkedIndex = startingItemsListBox.CheckedIndices[i];
                    var itemId = _allItems[checkedIndex].Id;
                    _game.StartingItems.Add(new StartingItemDefinition
                    {
                        ItemId = itemId,
                        Quantity = 1
                    });
                }

                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            buttonPanel.Controls.Add(cancelBtn);
            buttonPanel.Controls.Add(okBtn);

            this.Controls.Add(panel);
            this.Controls.Add(buttonPanel);
            this.CancelButton = cancelBtn;
            this.AcceptButton = okBtn;
        }
    }

    public class RoomEditorDialog : Form
    {
        public RoomDefinition CreatedRoom { get; private set; }
        private TextBox roomIdTextBox;
        private TextBox roomNameTextBox;
        private TextBox roomDescriptionTextBox;
        private ListBox exitsListBox;
        private List<ExitDefinition> _exits = new();
        private List<RoomDefinition> _allRooms;

        public RoomEditorDialog(RoomDefinition room = null, List<RoomDefinition> allRooms = null)
        {
            _allRooms = allRooms ?? new List<RoomDefinition>();
            InitializeDialog();

            if (room != null)
            {
                // Pre-fill with existing data
                roomIdTextBox.Text = room.Id;
                roomNameTextBox.Text = room.Name;
                roomDescriptionTextBox.Text = room.Description ?? "";
                _exits = room.Exits?.ToList() ?? new List<ExitDefinition>();
                UpdateExitsList();
            }
        }

        private void InitializeDialog()
        {
            this.Text = "Edit Room";
            this.Size = new System.Drawing.Size(700, 600);
            this.StartPosition = FormStartPosition.CenterParent;

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(10)
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

            // Top panel - basic room info
            var topPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(5)
            };
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            topPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));

            // Room ID
            topPanel.Controls.Add(new Label { Text = "Room ID:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            roomIdTextBox = new TextBox { Dock = DockStyle.Fill };
            topPanel.Controls.Add(roomIdTextBox, 1, 0);

            // Room Name
            topPanel.Controls.Add(new Label { Text = "Name:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            roomNameTextBox = new TextBox { Dock = DockStyle.Fill };
            topPanel.Controls.Add(roomNameTextBox, 1, 1);

            // Room Description
            topPanel.Controls.Add(new Label { Text = "Description:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 2);
            roomDescriptionTextBox = new TextBox { Multiline = true, Dock = DockStyle.Fill };
            topPanel.Controls.Add(roomDescriptionTextBox, 1, 2);

            // Bottom panel - exits management
            var bottomPanel = new GroupBox
            {
                Text = "Exits",
                Dock = DockStyle.Fill,
                Padding = new Padding(10)
            };

            var exitsLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            exitsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            exitsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

            exitsListBox = new ListBox { Dock = DockStyle.Fill };

            var exitsButtonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                Padding = new Padding(5)
            };

            var addExitBtn = new Button { Text = "Add Exit", Width = 100 };
            addExitBtn.Click += AddExit_Click;
            var editExitBtn = new Button { Text = "Edit Exit", Width = 100 };
            editExitBtn.Click += EditExit_Click;
            var deleteExitBtn = new Button { Text = "Delete Exit", Width = 100 };
            deleteExitBtn.Click += DeleteExit_Click;

            exitsButtonPanel.Controls.Add(addExitBtn);
            exitsButtonPanel.Controls.Add(editExitBtn);
            exitsButtonPanel.Controls.Add(deleteExitBtn);

            exitsLayout.Controls.Add(exitsListBox, 0, 0);
            exitsLayout.Controls.Add(exitsButtonPanel, 1, 0);
            bottomPanel.Controls.Add(exitsLayout);

            mainLayout.Controls.Add(topPanel, 0, 0);
            mainLayout.Controls.Add(bottomPanel, 0, 1);

            // Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var cancelBtn = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            var okBtn = new Button { Text = "OK", Width = 80 };
            okBtn.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(roomIdTextBox.Text) || string.IsNullOrWhiteSpace(roomNameTextBox.Text))
                {
                    MessageBox.Show("Please fill in Room ID and Name.");
                    this.DialogResult = DialogResult.None;
                    return;
                }

                CreatedRoom = new RoomDefinition
                {
                    Id = roomIdTextBox.Text,
                    Name = roomNameTextBox.Text,
                    Description = roomDescriptionTextBox.Text,
                    NPCs = new List<RoomNpcDefinition>(),
                    Items = new List<RoomItemDefinition>(),
                    Exits = _exits
                };

                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            buttonPanel.Controls.Add(cancelBtn);
            buttonPanel.Controls.Add(okBtn);

            this.Controls.Add(mainLayout);
            this.Controls.Add(buttonPanel);
            this.CancelButton = cancelBtn;
            this.AcceptButton = okBtn;
        }

        private void AddExit_Click(object sender, EventArgs e)
        {
            var dialog = new ExitEditorDialog(_allRooms);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _exits.Add(dialog.CreatedExit);
                UpdateExitsList();
            }
        }

        private void EditExit_Click(object sender, EventArgs e)
        {
            if (exitsListBox.SelectedIndex < 0) return;

            var exit = _exits[exitsListBox.SelectedIndex];
            var dialog = new ExitEditorDialog(_allRooms, exit);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _exits[exitsListBox.SelectedIndex] = dialog.CreatedExit;
                UpdateExitsList();
            }
        }

        private void DeleteExit_Click(object sender, EventArgs e)
        {
            if (exitsListBox.SelectedIndex < 0) return;

            _exits.RemoveAt(exitsListBox.SelectedIndex);
            UpdateExitsList();
        }

        private void UpdateExitsList()
        {
            exitsListBox.Items.Clear();
            foreach (var exit in _exits)
            {
                exitsListBox.Items.Add($"{exit.DisplayName} → {exit.DestinationRoomId}");
            }
        }
    }

    public class ExitEditorDialog : Form
    {
        public ExitDefinition CreatedExit { get; private set; }
        private TextBox displayNameTextBox;
        private ComboBox destinationRoomComboBox;
        private TextBox descriptionTextBox;
        private List<RoomDefinition> _allRooms;

        public ExitEditorDialog(List<RoomDefinition> allRooms, ExitDefinition exit = null)
        {
            _allRooms = allRooms ?? new List<RoomDefinition>();
            InitializeDialog();

            if (exit != null)
            {
                displayNameTextBox.Text = exit.DisplayName;
                destinationRoomComboBox.Text = exit.DestinationRoomId;
                descriptionTextBox.Text = exit.Description ?? "";
            }
        }

        private void InitializeDialog()
        {
            this.Text = "Edit Exit";
            this.Size = new System.Drawing.Size(500, 350);
            this.StartPosition = FormStartPosition.CenterParent;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 3,
                Padding = new Padding(10)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            // Display Name (what player sees)
            panel.Controls.Add(new Label { Text = "Exit Name:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            displayNameTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(displayNameTextBox, 1, 0);

            // Destination Room (dropdown)
            panel.Controls.Add(new Label { Text = "Destination:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            destinationRoomComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            foreach (var room in _allRooms)
            {
                destinationRoomComboBox.Items.Add($"{room.Id} - {room.Name}");
            }
            panel.Controls.Add(destinationRoomComboBox, 1, 1);

            // Description
            panel.Controls.Add(new Label { Text = "Description:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 2);
            descriptionTextBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, Height = 100 };
            panel.Controls.Add(descriptionTextBox, 1, 2);

            // Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var cancelBtn = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            var okBtn = new Button { Text = "OK", Width = 80 };
            okBtn.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(displayNameTextBox.Text))
                {
                    MessageBox.Show("Please enter an exit name.");
                    this.DialogResult = DialogResult.None;
                    return;
                }

                // Extract room ID from combo box selection (format: "room_id - Room Name")
                var destinationText = destinationRoomComboBox.Text;
                var destinationRoomId = destinationText.Contains(" - ")
                    ? destinationText.Substring(0, destinationText.IndexOf(" - "))
                    : destinationText;

                if (string.IsNullOrWhiteSpace(destinationRoomId))
                {
                    MessageBox.Show("Please select or enter a destination room.");
                    this.DialogResult = DialogResult.None;
                    return;
                }

                CreatedExit = new ExitDefinition
                {
                    Id = Guid.NewGuid().ToString(),
                    DisplayName = displayNameTextBox.Text,
                    DestinationRoomId = destinationRoomId,
                    Description = descriptionTextBox.Text
                };

                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            buttonPanel.Controls.Add(cancelBtn);
            buttonPanel.Controls.Add(okBtn);

            this.Controls.Add(panel);
            this.Controls.Add(buttonPanel);
            this.CancelButton = cancelBtn;
            this.AcceptButton = okBtn;
        }
    }

    public class NpcEditorDialog : Form
    {
        public NpcDefinition CreatedNpc { get; private set; }
        private TextBox npcIdTextBox;
        private TextBox npcNameTextBox;
        private TextBox npcTitleTextBox;
        private TextBox npcDescriptionTextBox;
        private TextBox healthTextBox;
        private TextBox strengthTextBox;
        private TextBox agilityTextBox;
        private TextBox armorTextBox;
        private ComboBox startingRoomComboBox;
        private CheckedListBox startingItemsListBox;
        private List<RoomDefinition> _allRooms;
        private List<ItemDefinition> _allItems;

        public NpcEditorDialog(NpcDefinition npc = null, List<RoomDefinition> allRooms = null, List<ItemDefinition> allItems = null)
        {
            _allRooms = allRooms ?? new List<RoomDefinition>();
            _allItems = allItems ?? new List<ItemDefinition>();
            InitializeDialog();

            if (npc != null)
            {
                // Pre-fill with existing data
                npcIdTextBox.Text = npc.Id;
                npcNameTextBox.Text = npc.Name;
                npcTitleTextBox.Text = npc.Title ?? "";
                npcDescriptionTextBox.Text = npc.Description ?? "";
                healthTextBox.Text = npc.Stats?.Health.ToString() ?? "60";
                strengthTextBox.Text = npc.Stats?.Strength.ToString() ?? "10";
                agilityTextBox.Text = npc.Stats?.Agility.ToString() ?? "10";
                armorTextBox.Text = npc.Stats?.Armor.ToString() ?? "0";

                // Set starting room
                if (npc.Location != null && !string.IsNullOrEmpty(npc.Location.CurrentRoomId))
                {
                    startingRoomComboBox.Text = npc.Location.CurrentRoomId;
                }

                // Set starting items
                if (npc.Inventory != null)
                {
                    for (int i = 0; i < startingItemsListBox.Items.Count; i++)
                    {
                        var itemId = _allItems[i].Id;
                        if (npc.Inventory.Any(inv => inv.ItemId == itemId))
                        {
                            startingItemsListBox.SetItemChecked(i, true);
                        }
                    }
                }
            }
        }

        private void InitializeDialog()
        {
            this.Text = "Edit NPC";
            this.Size = new System.Drawing.Size(650, 700);
            this.StartPosition = FormStartPosition.CenterParent;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 11,
                Padding = new Padding(10)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            // NPC ID
            panel.Controls.Add(new Label { Text = "NPC ID:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            npcIdTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(npcIdTextBox, 1, 0);

            // NPC Name
            panel.Controls.Add(new Label { Text = "Name:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            npcNameTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(npcNameTextBox, 1, 1);

            // NPC Title
            panel.Controls.Add(new Label { Text = "Title:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
            npcTitleTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(npcTitleTextBox, 1, 2);

            // NPC Description
            panel.Controls.Add(new Label { Text = "Description:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 3);
            npcDescriptionTextBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, Height = 60 };
            panel.Controls.Add(npcDescriptionTextBox, 1, 3);

            // Health
            panel.Controls.Add(new Label { Text = "Health:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 4);
            healthTextBox = new TextBox { Text = "60", Dock = DockStyle.Fill };
            panel.Controls.Add(healthTextBox, 1, 4);

            // Strength
            panel.Controls.Add(new Label { Text = "Strength:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 5);
            strengthTextBox = new TextBox { Text = "10", Dock = DockStyle.Fill };
            panel.Controls.Add(strengthTextBox, 1, 5);

            // Agility
            panel.Controls.Add(new Label { Text = "Agility:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 6);
            agilityTextBox = new TextBox { Text = "10", Dock = DockStyle.Fill };
            panel.Controls.Add(agilityTextBox, 1, 6);

            // Armor
            panel.Controls.Add(new Label { Text = "Armor:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 7);
            armorTextBox = new TextBox { Text = "0", Dock = DockStyle.Fill };
            panel.Controls.Add(armorTextBox, 1, 7);

            // Starting Room
            panel.Controls.Add(new Label { Text = "Starting Room:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 8);
            startingRoomComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            foreach (var room in _allRooms)
            {
                startingRoomComboBox.Items.Add($"{room.Id} - {room.Name}");
            }
            panel.Controls.Add(startingRoomComboBox, 1, 8);

            // Starting Items
            panel.Controls.Add(new Label { Text = "Starting Items:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 9);
            startingItemsListBox = new CheckedListBox { Dock = DockStyle.Fill, Height = 120 };
            foreach (var item in _allItems)
            {
                startingItemsListBox.Items.Add($"{item.Name} ({item.Id})");
            }
            panel.Controls.Add(startingItemsListBox, 1, 9);

            // Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var cancelBtn = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            var okBtn = new Button { Text = "OK", Width = 80 };
            okBtn.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(npcIdTextBox.Text) || string.IsNullOrWhiteSpace(npcNameTextBox.Text))
                {
                    MessageBox.Show("Please fill in NPC ID and Name.");
                    this.DialogResult = DialogResult.None;
                    return;
                }

                // Parse stats with validation
                if (!int.TryParse(healthTextBox.Text, out int health) ||
                    !int.TryParse(strengthTextBox.Text, out int strength) ||
                    !int.TryParse(agilityTextBox.Text, out int agility) ||
                    !int.TryParse(armorTextBox.Text, out int armor))
                {
                    MessageBox.Show("Please enter valid numbers for all stats.");
                    this.DialogResult = DialogResult.None;
                    return;
                }

                // Extract starting room ID
                var startingRoomText = startingRoomComboBox.Text;
                var startingRoomId = startingRoomText.Contains(" - ")
                    ? startingRoomText.Substring(0, startingRoomText.IndexOf(" - "))
                    : startingRoomText;

                // Build inventory from checked items
                var inventory = new List<InventoryItemDefinition>();
                for (int i = 0; i < startingItemsListBox.CheckedItems.Count; i++)
                {
                    var checkedIndex = startingItemsListBox.CheckedIndices[i];
                    var itemId = _allItems[checkedIndex].Id;
                    inventory.Add(new InventoryItemDefinition
                    {
                        ItemId = itemId,
                        Quantity = 1,
                        Loot = true
                    });
                }

                CreatedNpc = new NpcDefinition
                {
                    Id = npcIdTextBox.Text,
                    Name = npcNameTextBox.Text,
                    Title = npcTitleTextBox.Text,
                    Description = npcDescriptionTextBox.Text,
                    Stats = new NpcStatsDefinition
                    {
                        Health = health,
                        MaxHealth = health,
                        Level = 1,
                        Strength = strength,
                        Agility = agility,
                        Armor = armor
                    },
                    Location = new LocationDefinition
                    {
                        CurrentRoomId = startingRoomId,
                        HomeRoomId = startingRoomId,
                        CanMove = true
                    },
                    Inventory = inventory
                };

                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            buttonPanel.Controls.Add(cancelBtn);
            buttonPanel.Controls.Add(okBtn);

            this.Controls.Add(panel);
            this.Controls.Add(buttonPanel);
            this.CancelButton = cancelBtn;
            this.AcceptButton = okBtn;
        }
    }

    public class WinConditionEditorDialog : Form
    {
        public WinConditionDefinition CreatedWinCondition { get; private set; }
        private TextBox conditionIdTextBox;
        private TextBox descriptionTextBox;
        private ComboBox typeComboBox;
        private TextBox targetIdTextBox;
        private TextBox victoryNarrationTextBox;
        private TextBox victoryMessageTextBox;

        public WinConditionEditorDialog()
        {
            InitializeDialog();
        }

        private void InitializeDialog()
        {
            this.Text = "Edit Win Condition";
            this.Size = new System.Drawing.Size(600, 500);
            this.StartPosition = FormStartPosition.CenterParent;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                Padding = new Padding(10)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

            // Condition ID
            panel.Controls.Add(new Label { Text = "Condition ID:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            conditionIdTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(conditionIdTextBox, 1, 0);

            // Description
            panel.Controls.Add(new Label { Text = "Description:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 1);
            descriptionTextBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, Height = 60 };
            panel.Controls.Add(descriptionTextBox, 1, 1);

            // Type (room, item, npc_defeat, quest_complete)
            panel.Controls.Add(new Label { Text = "Type:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 2);
            typeComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            typeComboBox.Items.AddRange(new object[] { "room", "item", "npc_defeat", "quest_complete" });
            typeComboBox.SelectedIndex = 0;
            panel.Controls.Add(typeComboBox, 1, 2);

            // Target ID
            panel.Controls.Add(new Label { Text = "Target ID:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 3);
            targetIdTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(targetIdTextBox, 1, 3);

            // Victory Narration (for LLM)
            panel.Controls.Add(new Label { Text = "Victory Narration:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 4);
            victoryNarrationTextBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, Height = 60 };
            panel.Controls.Add(victoryNarrationTextBox, 1, 4);

            // Victory Message (fallback)
            panel.Controls.Add(new Label { Text = "Victory Message:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 5);
            victoryMessageTextBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, Height = 60 };
            panel.Controls.Add(victoryMessageTextBox, 1, 5);

            // Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var cancelBtn = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            var okBtn = new Button { Text = "OK", Width = 80 };
            okBtn.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(conditionIdTextBox.Text) || string.IsNullOrWhiteSpace(descriptionTextBox.Text))
                {
                    MessageBox.Show("Please fill in Condition ID and Description.");
                    this.DialogResult = DialogResult.None;
                    return;
                }

                CreatedWinCondition = new WinConditionDefinition
                {
                    Id = conditionIdTextBox.Text,
                    Description = descriptionTextBox.Text,
                    Type = typeComboBox.SelectedItem?.ToString() ?? "room",
                    TargetId = string.IsNullOrWhiteSpace(targetIdTextBox.Text) ? null : targetIdTextBox.Text,
                    VictoryNarration = string.IsNullOrWhiteSpace(victoryNarrationTextBox.Text) ? null : victoryNarrationTextBox.Text,
                    VictoryMessage = string.IsNullOrWhiteSpace(victoryMessageTextBox.Text) ? null : victoryMessageTextBox.Text
                };

                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            buttonPanel.Controls.Add(cancelBtn);
            buttonPanel.Controls.Add(okBtn);

            this.Controls.Add(panel);
            this.Controls.Add(buttonPanel);
            this.CancelButton = cancelBtn;
            this.AcceptButton = okBtn;
        }
    }

    public class ItemEditorDialog : Form
    {
        public ItemDefinition CreatedItem { get; private set; }
        private TextBox itemIdTextBox;
        private TextBox itemNameTextBox;
        private TextBox itemDescriptionTextBox;
        private ComboBox typeComboBox;
        private TextBox damageTextBox;
        private TextBox armorBonusTextBox;

        public ItemEditorDialog()
        {
            InitializeDialog();
        }

        public ItemEditorDialog(ItemDefinition item)
        {
            InitializeDialog();
            // Pre-fill with existing data
            itemIdTextBox.Text = item.Id;
            itemNameTextBox.Text = item.Name;
            itemDescriptionTextBox.Text = item.Description ?? "";
            typeComboBox.SelectedItem = item.Type ?? "misc";
            damageTextBox.Text = item.Metadata?.ContainsKey("damage") == true
                ? item.Metadata["damage"].ToString() ?? "0"
                : "0";
            armorBonusTextBox.Text = item.Metadata?.ContainsKey("armorBonus") == true
                ? item.Metadata["armorBonus"].ToString() ?? "0"
                : "0";
        }

        private void InitializeDialog()
        {
            this.Text = "Edit Item";
            this.Size = new System.Drawing.Size(550, 450);
            this.StartPosition = FormStartPosition.CenterParent;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 7,
                Padding = new Padding(10)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65));

            // Item ID
            panel.Controls.Add(new Label { Text = "Item ID:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            itemIdTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(itemIdTextBox, 1, 0);

            // Item Name
            panel.Controls.Add(new Label { Text = "Name:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            itemNameTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(itemNameTextBox, 1, 1);

            // Item Description
            panel.Controls.Add(new Label { Text = "Description:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 2);
            itemDescriptionTextBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, Height = 80 };
            panel.Controls.Add(itemDescriptionTextBox, 1, 2);

            // Type (weapon, armor, consumable, key, misc)
            panel.Controls.Add(new Label { Text = "Type:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 3);
            typeComboBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            typeComboBox.Items.AddRange(new object[] { "weapon", "armor", "consumable", "key", "misc" });
            typeComboBox.SelectedIndex = 0;
            panel.Controls.Add(typeComboBox, 1, 3);

            // Damage (for weapons)
            panel.Controls.Add(new Label { Text = "Damage:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 4);
            damageTextBox = new TextBox { Text = "0", Dock = DockStyle.Fill };
            panel.Controls.Add(damageTextBox, 1, 4);

            // Armor Bonus (for armor)
            panel.Controls.Add(new Label { Text = "Armor Bonus:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 5);
            armorBonusTextBox = new TextBox { Text = "0", Dock = DockStyle.Fill };
            panel.Controls.Add(armorBonusTextBox, 1, 5);

            // Buttons
            var buttonPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10)
            };

            var cancelBtn = new Button { Text = "Cancel", Width = 80, DialogResult = DialogResult.Cancel };
            var okBtn = new Button { Text = "OK", Width = 80 };
            okBtn.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(itemIdTextBox.Text) || string.IsNullOrWhiteSpace(itemNameTextBox.Text))
                {
                    MessageBox.Show("Please fill in Item ID and Name.");
                    this.DialogResult = DialogResult.None;
                    return;
                }

                if (!int.TryParse(damageTextBox.Text, out int damage) ||
                    !int.TryParse(armorBonusTextBox.Text, out int armorBonus))
                {
                    MessageBox.Show("Please enter valid numbers for Damage and Armor Bonus.");
                    this.DialogResult = DialogResult.None;
                    return;
                }

                var metadata = new Dictionary<string, object>();
                if (damage > 0)
                    metadata["damage"] = damage;
                if (armorBonus > 0)
                    metadata["armorBonus"] = armorBonus;

                CreatedItem = new ItemDefinition
                {
                    Id = itemIdTextBox.Text,
                    Name = itemNameTextBox.Text,
                    Description = itemDescriptionTextBox.Text,
                    Type = typeComboBox.SelectedItem?.ToString() ?? "misc",
                    Metadata = metadata
                };

                this.DialogResult = DialogResult.OK;
                this.Close();
            };

            buttonPanel.Controls.Add(cancelBtn);
            buttonPanel.Controls.Add(okBtn);

            this.Controls.Add(panel);
            this.Controls.Add(buttonPanel);
            this.CancelButton = cancelBtn;
            this.AcceptButton = okBtn;
        }
    }
}
