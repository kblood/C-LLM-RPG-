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

        private MenuStrip _menuStrip;
        private TreeView _gameTreeView;
        private TabControl _tabControl;
        private SplitContainer _mainPanel;

        public GameEditorForm()
        {
            InitializeComponent();
            ConfigureForm();
            CreateControls();
            this.Load += (s, e) => OnFormLoad();
        }

        private void OnFormLoad()
        {
            LoadGameList();
        }

        private void ConfigureForm()
        {
            this.Text = "RPG Game Editor";
            this.Width = 1200;
            this.Height = 800;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.AutoScaleMode = AutoScaleMode.Font;
            this.MinimumSize = new System.Drawing.Size(600, 400);
            this.SuspendLayout();
        }

        private void CreateControls()
        {
            // Create menu strip FIRST
            _menuStrip = new MenuStrip();
            var fileMenu = new ToolStripMenuItem("&File");
            fileMenu.DropDownItems.Add("&New Game", null, NewGame_Click);
            fileMenu.DropDownItems.Add("&Open Game", null, OpenGame_Click);
            fileMenu.DropDownItems.Add("&Save", null, SaveGame_Click);
            fileMenu.DropDownItems.Add("&Save As", null, SaveGameAs_Click);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add("E&xit", null, (s, e) => this.Close());
            _menuStrip.Items.Add(fileMenu);

            var editMenu = new ToolStripMenuItem("&Edit");
            editMenu.DropDownItems.Add("&Game Properties", null, EditGameProperties_Click);
            _menuStrip.Items.Add(editMenu);

            var helpMenu = new ToolStripMenuItem("&Help");
            helpMenu.DropDownItems.Add("&About", null, (s, e) =>
                MessageBox.Show("RPG Game Editor v1.0\n\nCreate and edit JSON-based RPG games.", "About"));
            _menuStrip.Items.Add(helpMenu);

            this.MainMenuStrip = _menuStrip;
            this.Controls.Add(_menuStrip);

            // Create a container panel that will hold the split container
            // This ensures proper docking with the menu strip
            var containerPanel = new Panel
            {
                Dock = DockStyle.Fill,
                Name = "ContainerPanel"
            };

            // Create split container for main content
            _mainPanel = new SplitContainer
            {
                Orientation = Orientation.Vertical,
                SplitterDistance = 250,
                SplitterWidth = 4,
                Dock = DockStyle.Fill,
                Name = "MainSplitContainer"
            };

            // Left panel - TreeView
            _gameTreeView = new TreeView
            {
                Dock = DockStyle.Fill,
                Name = "GameTreeView"
            };
            _mainPanel.Panel1.Controls.Add(_gameTreeView);

            // Right panel - TabControl
            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Name = "EditorTabs"
            };
            _tabControl.TabPages.Add(new TabPage("Game Info") { Name = "GameInfoTab" });
            _tabControl.TabPages.Add(new TabPage("Rooms") { Name = "RoomsTab" });
            _tabControl.TabPages.Add(new TabPage("NPCs") { Name = "NPCsTab" });
            _tabControl.TabPages.Add(new TabPage("Items") { Name = "ItemsTab" });
            _tabControl.TabPages.Add(new TabPage("Quests") { Name = "QuestsTab" });

            _mainPanel.Panel2.Controls.Add(_tabControl);

            containerPanel.Controls.Add(_mainPanel);
            this.Controls.Add(containerPanel);

            this.ResumeLayout(false);
            this.PerformLayout();
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
                    MessageBox.Show("Game loaded successfully!");
                    UpdateUI();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading game: {ex.Message}");
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
                var gameJsonPath = Path.Combine(_currentGamePath, "game.json");
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_currentGame, options);
                File.WriteAllText(gameJsonPath, json);
                MessageBox.Show("Game saved successfully!");
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

            var propsDialog = new GamePropertiesDialog(_currentGame);
            if (propsDialog.ShowDialog() == DialogResult.OK)
            {
                MessageBox.Show("Game properties updated!");
                UpdateUI();
            }
        }

        private void UpdateUI()
        {
            // TODO: Update UI elements to reflect current game state
            this.Text = $"RPG Game Editor - {_currentGame?.Title ?? "No Game Loaded"}";
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

        public GamePropertiesDialog(GameDefinition game)
        {
            _game = game;
            InitializeDialog();
        }

        private void InitializeDialog()
        {
            this.Text = "Game Properties";
            this.Size = new System.Drawing.Size(500, 400);
            this.StartPosition = FormStartPosition.CenterParent;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                Padding = new Padding(10)
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
            var descBox = new TextBox { Text = _game.Description ?? "", Multiline = true, Dock = DockStyle.Fill };
            panel.Controls.Add(descBox, 1, 2);

            // Starting Room
            panel.Controls.Add(new Label { Text = "Starting Room:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 3);
            var startRoomBox = new TextBox { Text = _game.GameSettings?.StartingRoomId ?? "start", Dock = DockStyle.Fill };
            panel.Controls.Add(startRoomBox, 1, 3);

            // Starting Health
            panel.Controls.Add(new Label { Text = "Starting Health:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 4);
            var healthBox = new TextBox { Text = (_game.GameSettings?.PlayerStartingHealth ?? 100).ToString(), Dock = DockStyle.Fill };
            panel.Controls.Add(healthBox, 1, 4);

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
                if (_game.GameSettings == null)
                    _game.GameSettings = new GameSettingsDefinition();
                _game.GameSettings.StartingRoomId = startRoomBox.Text;
                if (int.TryParse(healthBox.Text, out int health))
                    _game.GameSettings.PlayerStartingHealth = health;
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

        public RoomEditorDialog()
        {
            InitializeDialog();
        }

        private void InitializeDialog()
        {
            this.Text = "Edit Room";
            this.Size = new System.Drawing.Size(500, 400);
            this.StartPosition = FormStartPosition.CenterParent;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(10)
            };

            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));

            // Room ID
            panel.Controls.Add(new Label { Text = "Room ID:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
            roomIdTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(roomIdTextBox, 1, 0);

            // Room Name
            panel.Controls.Add(new Label { Text = "Name:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
            roomNameTextBox = new TextBox { Dock = DockStyle.Fill };
            panel.Controls.Add(roomNameTextBox, 1, 1);

            // Room Description
            panel.Controls.Add(new Label { Text = "Description:", TextAlign = System.Drawing.ContentAlignment.TopLeft }, 0, 2);
            roomDescriptionTextBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, Height = 100 };
            panel.Controls.Add(roomDescriptionTextBox, 1, 2);

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
                    Exits = new List<ExitDefinition>()
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

        public NpcEditorDialog()
        {
            InitializeDialog();
        }

        private void InitializeDialog()
        {
            this.Text = "Edit NPC";
            this.Size = new System.Drawing.Size(600, 550);
            this.StartPosition = FormStartPosition.CenterParent;

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 9,
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
            npcDescriptionTextBox = new TextBox { Multiline = true, Dock = DockStyle.Fill, Height = 80 };
            panel.Controls.Add(npcDescriptionTextBox, 1, 3);

            // Health
            panel.Controls.Add(new Label { Text = "Health:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 4);
            healthTextBox = new TextBox { Text = "60", Dock = DockStyle.Fill };
            panel.Controls.Add(healthTextBox, 1, 4);

            // Strength
            panel.Controls.Add(new Label { Text = "Strength:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 5);
            strengthTextBox = new TextBox { Text = "10", Dock = DockStyle.Fill };
            panel.Controls.Add(strengthTextBox, 1, 5);

            // Agility (for fleeing)
            panel.Controls.Add(new Label { Text = "Agility:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 6);
            agilityTextBox = new TextBox { Text = "10", Dock = DockStyle.Fill };
            panel.Controls.Add(agilityTextBox, 1, 6);

            // Armor
            panel.Controls.Add(new Label { Text = "Armor:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 7);
            armorTextBox = new TextBox { Text = "0", Dock = DockStyle.Fill };
            panel.Controls.Add(armorTextBox, 1, 7);

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
                    Inventory = new List<InventoryItemDefinition>()
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
