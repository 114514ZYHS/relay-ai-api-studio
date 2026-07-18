using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Relay AI API Studio")]
[assembly: System.Reflection.AssemblyDescription("Small native Windows AI API client")]
[assembly: System.Reflection.AssemblyCompany("Relay")]
[assembly: System.Reflection.AssemblyProduct("Relay AI API Studio")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace RelayAIStudio
{
    internal sealed class Provider
    {
        public string Id;
        public string Name;
        public string BaseUrl;
        public string Protocol;
        public bool RequiresKey;
        public string[] Models;

        public Provider(string id, string name, string baseUrl, string protocol, bool requiresKey, params string[] models)
        {
            Id = id; Name = name; BaseUrl = baseUrl; Protocol = protocol; RequiresKey = requiresKey; Models = models;
        }
    }

    internal sealed class ChatMessage
    {
        public string Role;
        public string Content;
        public ChatMessage(string role, string content) { Role = role; Content = content; }
    }

    internal sealed class PromptTemplate
    {
        public string Category;
        public string Title;
        public string Description;
        public string Text;
        public PromptTemplate(string category, string title, string description, string text)
        {
            Category = category; Title = title; Description = description; Text = text;
        }
        public override string ToString() { return Title; }
    }

    internal static class JsonTools
    {
        public static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };

        public static Dictionary<string, object> Dict(object value)
        {
            return value as Dictionary<string, object>;
        }

        public static object Get(Dictionary<string, object> dictionary, string key)
        {
            if (dictionary == null) return null;
            object value;
            return dictionary.TryGetValue(key, out value) ? value : null;
        }

        public static string String(object value)
        {
            return value == null ? "" : Convert.ToString(value);
        }

        public static List<string> ParseModels(string json)
        {
            object rootObject = Serializer.DeserializeObject(json);
            Dictionary<string, object> root = Dict(rootObject);
            object source = Get(root, "data") ?? Get(root, "models");
            object[] items = source as object[];
            List<string> models = new List<string>();
            if (items == null) return models;
            foreach (object item in items)
            {
                string id = item as string;
                if (id == null)
                {
                    Dictionary<string, object> model = Dict(item);
                    id = String(Get(model, "id"));
                    if (id.Length == 0) id = String(Get(model, "name"));
                }
                if (id.StartsWith("models/", StringComparison.OrdinalIgnoreCase)) id = id.Substring(7);
                if (id.Length > 0 && !models.Contains(id)) models.Add(id);
            }
            models.Sort(StringComparer.OrdinalIgnoreCase);
            return models;
        }

        public static string ExtractText(string json, string protocol)
        {
            Dictionary<string, object> root = Dict(Serializer.DeserializeObject(json));
            if (root == null) return "";
            if (protocol == "responses")
            {
                string direct = String(Get(root, "output_text"));
                if (direct.Length > 0) return direct;
                return ExtractResponseOutput(root);
            }
            object[] choices = Get(root, "choices") as object[];
            if (choices == null || choices.Length == 0) return "";
            Dictionary<string, object> choice = Dict(choices[0]);
            Dictionary<string, object> message = Dict(Get(choice, "message"));
            return String(Get(message, "content"));
        }

        private static string ExtractResponseOutput(Dictionary<string, object> root)
        {
            StringBuilder result = new StringBuilder();
            object[] output = Get(root, "output") as object[];
            if (output == null) return "";
            foreach (object outputItem in output)
            {
                Dictionary<string, object> item = Dict(outputItem);
                object[] content = Get(item, "content") as object[];
                if (content == null) continue;
                foreach (object contentItem in content)
                {
                    Dictionary<string, object> part = Dict(contentItem);
                    if (String(Get(part, "type")) == "output_text") result.Append(String(Get(part, "text")));
                }
            }
            return result.ToString();
        }

        public static string ExtractStreamDelta(string json, string protocol)
        {
            Dictionary<string, object> root = Dict(Serializer.DeserializeObject(json));
            if (root == null) return "";
            if (protocol == "responses")
            {
                return String(Get(root, "type")) == "response.output_text.delta" ? String(Get(root, "delta")) : "";
            }
            object[] choices = Get(root, "choices") as object[];
            if (choices == null || choices.Length == 0) return "";
            Dictionary<string, object> choice = Dict(choices[0]);
            Dictionary<string, object> delta = Dict(Get(choice, "delta"));
            return String(Get(delta, "content"));
        }

    }

    internal sealed class PromptLibraryForm : Form
    {
        private readonly List<PromptTemplate> allTemplates;
        private readonly TextBox searchBox = new TextBox();
        private readonly ComboBox categoryBox = new ComboBox();
        private readonly ListBox templateList = new ListBox();
        private readonly TextBox previewBox = new TextBox();
        private readonly Button insertButton = new Button();
        public string SelectedPrompt { get; private set; }

        public PromptLibraryForm(List<PromptTemplate> templates)
        {
            allTemplates = templates;
            Text = "提示词库";
            Font = new Font("Microsoft YaHei UI", 9F);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(660, 520);
            Size = new Size(760, 590);
            BackColor = Color.FromArgb(246, 247, 245);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(16);
            layout.ColumnCount = 2;
            layout.RowCount = 3;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
            Controls.Add(layout);

            searchBox.Dock = DockStyle.Fill;
            searchBox.Font = new Font("Microsoft YaHei UI", 10F);
            searchBox.Margin = new Padding(0, 4, 8, 4);
            layout.Controls.Add(searchBox, 0, 0);

            categoryBox.Dock = DockStyle.Fill;
            categoryBox.DropDownStyle = ComboBoxStyle.DropDownList;
            categoryBox.Items.AddRange(new object[] { "全部", "编程", "产品", "分析", "写作", "学习", "创意" });
            categoryBox.SelectedIndex = 0;
            categoryBox.Margin = new Padding(8, 4, 0, 4);
            layout.Controls.Add(categoryBox, 1, 0);

            templateList.Dock = DockStyle.Fill;
            templateList.BorderStyle = BorderStyle.FixedSingle;
            templateList.IntegralHeight = false;
            templateList.Margin = new Padding(0, 8, 8, 8);
            layout.Controls.Add(templateList, 0, 1);

            previewBox.Dock = DockStyle.Fill;
            previewBox.Multiline = true;
            previewBox.ReadOnly = true;
            previewBox.ScrollBars = ScrollBars.Vertical;
            previewBox.BackColor = Color.White;
            previewBox.Margin = new Padding(8, 8, 0, 8);
            layout.Controls.Add(previewBox, 1, 1);

            Label hint = new Label();
            hint.Text = "选择模板后可继续编辑";
            hint.ForeColor = Color.FromArgb(104, 112, 105);
            hint.TextAlign = ContentAlignment.MiddleLeft;
            hint.Dock = DockStyle.Fill;
            layout.Controls.Add(hint, 0, 2);

            insertButton.Text = "插入提示词";
            insertButton.Dock = DockStyle.Right;
            insertButton.Width = 120;
            StylePrimaryButton(insertButton);
            layout.Controls.Add(insertButton, 1, 2);

            searchBox.TextChanged += delegate { RefreshTemplates(); };
            categoryBox.SelectedIndexChanged += delegate { RefreshTemplates(); };
            templateList.SelectedIndexChanged += delegate { UpdatePreview(); };
            templateList.DoubleClick += delegate { AcceptPrompt(); };
            insertButton.Click += delegate { AcceptPrompt(); };
            RefreshTemplates();
        }

        private static void StylePrimaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Color.FromArgb(24, 93, 70);
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
        }

        private void RefreshTemplates()
        {
            string query = searchBox.Text.Trim();
            string category = categoryBox.SelectedItem == null ? "全部" : categoryBox.SelectedItem.ToString();
            List<PromptTemplate> filtered = allTemplates.Where(delegate(PromptTemplate item)
            {
                bool categoryMatch = category == "全部" || item.Category == category;
                bool queryMatch = query.Length == 0 || item.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || item.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || item.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
                return categoryMatch && queryMatch;
            }).ToList();
            templateList.BeginUpdate();
            templateList.Items.Clear();
            foreach (PromptTemplate item in filtered) templateList.Items.Add(item);
            templateList.EndUpdate();
            if (templateList.Items.Count > 0) templateList.SelectedIndex = 0;
            else previewBox.Clear();
        }

        private void UpdatePreview()
        {
            PromptTemplate item = templateList.SelectedItem as PromptTemplate;
            previewBox.Text = item == null ? "" : item.Description + Environment.NewLine + Environment.NewLine + item.Text;
        }

        private void AcceptPrompt()
        {
            PromptTemplate item = templateList.SelectedItem as PromptTemplate;
            if (item == null) return;
            SelectedPrompt = item.Text;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color Surface = Color.White;
        private static readonly Color Background = Color.FromArgb(244, 245, 242);
        private static readonly Color Primary = Color.FromArgb(24, 93, 70);
        private static readonly Color PrimarySoft = Color.FromArgb(229, 241, 235);
        private static readonly Color TextColor = Color.FromArgb(28, 31, 28);
        private static readonly Color Muted = Color.FromArgb(103, 112, 104);
        private static readonly HttpClient Client = CreateHttpClient();

        private readonly JavaScriptSerializer serializer = JsonTools.Serializer;
        private readonly List<Provider> providers = CreateProviders();
        private readonly List<PromptTemplate> templates = CreateTemplates();
        private readonly List<ChatMessage> messages = new List<ChatMessage>();
        private readonly Dictionary<string, string> keys = new Dictionary<string, string>();

        private readonly ListBox providerList = new ListBox();
        private readonly TextBox baseUrlBox = new TextBox();
        private readonly TextBox apiKeyBox = new TextBox();
        private readonly ComboBox protocolBox = new ComboBox();
        private readonly ComboBox modelBox = new ComboBox();
        private readonly Label modelCountLabel = new Label();
        private readonly Label modelTypeLabel = new Label();
        private readonly Button syncModelsButton = new Button();
        private readonly TextBox systemPromptBox = new TextBox();
        private readonly NumericUpDown temperatureBox = new NumericUpDown();
        private readonly NumericUpDown maxTokensBox = new NumericUpDown();
        private readonly ComboBox reasoningBox = new ComboBox();
        private readonly CheckBox streamCheck = new CheckBox();
        private readonly TextBox customHeadersBox = new TextBox();
        private readonly RichTextBox chatBox = new RichTextBox();
        private readonly TextBox rawBox = new TextBox();
        private readonly TextBox requestBox = new TextBox();
        private readonly TextBox promptBox = new TextBox();
        private readonly Button sendButton = new Button();
        private readonly Button promptLibraryButton = new Button();
        private readonly ToolStripStatusLabel statusLabel = new ToolStripStatusLabel();
        private readonly Label headerStatus = new Label();
        private SplitContainer outerSplit;
        private SplitContainer contentSplit;
        private CancellationTokenSource sendCancellation;
        private Provider currentProvider;

        public MainForm()
        {
            Text = "Relay AI API Studio";
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Background;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(980, 650);
            Size = new Size(1220, 790);
            AutoScaleMode = AutoScaleMode.Dpi;

            BuildInterface();
            LoadProviders();
            Shown += delegate { ApplySplitLayout(); };
            Resize += delegate { ApplySplitLayout(); };
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler();
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            HttpClient client = new HttpClient(handler);
            client.Timeout = TimeSpan.FromMinutes(10);
            return client;
        }

        private void BuildInterface()
        {
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 58;
            header.BackColor = Surface;
            Controls.Add(header);

            Label mark = new Label();
            mark.Text = "R";
            mark.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            mark.ForeColor = Color.White;
            mark.BackColor = Primary;
            mark.TextAlign = ContentAlignment.MiddleCenter;
            mark.SetBounds(18, 13, 32, 32);
            header.Controls.Add(mark);

            Label title = new Label();
            title.Text = "Relay";
            title.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            title.ForeColor = TextColor;
            title.AutoSize = true;
            title.Location = new Point(60, 18);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "NATIVE API STUDIO";
            subtitle.Font = new Font("Segoe UI", 7.5F);
            subtitle.ForeColor = Muted;
            subtitle.AutoSize = true;
            subtitle.Location = new Point(112, 22);
            header.Controls.Add(subtitle);

            headerStatus.Text = "就绪";
            headerStatus.ForeColor = Primary;
            headerStatus.AutoSize = true;
            headerStatus.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            headerStatus.Location = new Point(ClientSize.Width - 70, 20);
            headerStatus.Resize += delegate { headerStatus.Left = header.ClientSize.Width - headerStatus.Width - 18; };
            header.SizeChanged += delegate { headerStatus.Left = header.ClientSize.Width - headerStatus.Width - 18; };
            header.Controls.Add(headerStatus);

            StatusStrip statusStrip = new StatusStrip();
            statusStrip.SizingGrip = false;
            statusStrip.BackColor = Surface;
            statusLabel.Text = "本地原生运行 · 密钥仅保存在内存";
            statusStrip.Items.Add(statusLabel);
            Controls.Add(statusStrip);

            SplitContainer outer = new SplitContainer();
            outerSplit = outer;
            outer.Dock = DockStyle.Fill;
            outer.FixedPanel = FixedPanel.Panel1;
            outer.SplitterWidth = 1;
            outer.BackColor = Color.FromArgb(221, 226, 221);
            Controls.Add(outer);
            outer.SizeChanged += delegate { ApplySplitLayout(); };
            outer.BringToFront();

            BuildProviderPanel(outer.Panel1);

            SplitContainer content = new SplitContainer();
            contentSplit = content;
            content.Dock = DockStyle.Fill;
            content.FixedPanel = FixedPanel.Panel2;
            content.SplitterWidth = 1;
            content.BackColor = Color.FromArgb(221, 226, 221);
            outer.Panel2.Controls.Add(content);
            content.SizeChanged += delegate { ApplySplitLayout(); };

            BuildConversationPanel(content.Panel1);
            BuildSettingsPanel(content.Panel2);
        }

        private void ApplySplitLayout()
        {
            if (outerSplit != null && outerSplit.ClientSize.Width >= 921)
            {
                int desiredOuter = 220;
                int maximumOuter = outerSplit.ClientSize.Width - 700 - outerSplit.SplitterWidth;
                if (desiredOuter <= maximumOuter && outerSplit.SplitterDistance != desiredOuter) outerSplit.SplitterDistance = desiredOuter;
            }
            if (contentSplit != null && contentSplit.ClientSize.Width >= 751)
            {
                int desiredContent = contentSplit.ClientSize.Width - 330 - contentSplit.SplitterWidth;
                int maximumContent = contentSplit.ClientSize.Width - 315 - contentSplit.SplitterWidth;
                if (desiredContent >= 420 && desiredContent <= maximumContent && contentSplit.SplitterDistance != desiredContent) contentSplit.SplitterDistance = desiredContent;
            }
        }

        private void BuildProviderPanel(Control parent)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.FromArgb(248, 249, 247);
            panel.Padding = new Padding(12, 14, 12, 12);
            parent.Controls.Add(panel);

            Button newChat = new Button();
            newChat.Text = "新建会话";
            newChat.Dock = DockStyle.Top;
            newChat.Height = 40;
            StylePrimaryButton(newChat);
            newChat.Click += delegate { ClearConversation(); };
            panel.Controls.Add(newChat);

            Label label = new Label();
            label.Text = "服务商";
            label.ForeColor = Muted;
            label.Dock = DockStyle.Top;
            label.Height = 34;
            label.Padding = new Padding(4, 12, 0, 0);
            panel.Controls.Add(label);
            label.BringToFront();
            newChat.BringToFront();

            providerList.Dock = DockStyle.Fill;
            providerList.BorderStyle = BorderStyle.None;
            providerList.BackColor = panel.BackColor;
            providerList.DrawMode = DrawMode.OwnerDrawFixed;
            providerList.ItemHeight = 47;
            providerList.IntegralHeight = false;
            providerList.DrawItem += DrawProviderItem;
            providerList.SelectedIndexChanged += ProviderChanged;
            panel.Controls.Add(providerList);
            providerList.BringToFront();
        }

        private void BuildConversationPanel(Control parent)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Surface;
            parent.Controls.Add(panel);

            TableLayoutPanel conversationLayout = new TableLayoutPanel();
            conversationLayout.Dock = DockStyle.Fill;
            conversationLayout.ColumnCount = 1;
            conversationLayout.RowCount = 2;
            conversationLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            conversationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 156F));
            panel.Controls.Add(conversationLayout);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Font = new Font("Microsoft YaHei UI", 9F);
            conversationLayout.Controls.Add(tabs, 0, 0);

            TabPage chatTab = new TabPage("对话");
            TabPage requestTab = new TabPage("请求体");
            TabPage rawTab = new TabPage("原始响应");
            tabs.TabPages.Add(chatTab);
            tabs.TabPages.Add(requestTab);
            tabs.TabPages.Add(rawTab);

            chatBox.Dock = DockStyle.Fill;
            chatBox.BorderStyle = BorderStyle.None;
            chatBox.ReadOnly = true;
            chatBox.BackColor = Surface;
            chatBox.Font = new Font("Microsoft YaHei UI", 10F);
            chatBox.DetectUrls = true;
            chatTab.Controls.Add(chatBox);

            ConfigureInspectorBox(requestBox);
            requestTab.Controls.Add(requestBox);
            ConfigureInspectorBox(rawBox);
            rawTab.Controls.Add(rawBox);

            Panel composer = new Panel();
            composer.Dock = DockStyle.Fill;
            composer.Padding = new Padding(16, 12, 16, 14);
            composer.BackColor = Color.FromArgb(249, 250, 248);
            conversationLayout.Controls.Add(composer, 0, 1);

            promptBox.Multiline = true;
            promptBox.ScrollBars = ScrollBars.Vertical;
            promptBox.Dock = DockStyle.Fill;
            promptBox.Font = new Font("Microsoft YaHei UI", 10F);
            promptBox.AcceptsReturn = true;
            promptBox.KeyDown += PromptKeyDown;
            composer.Controls.Add(promptBox);

            Panel actions = new Panel();
            actions.Dock = DockStyle.Right;
            actions.Width = 116;
            actions.Padding = new Padding(10, 0, 0, 0);
            composer.Controls.Add(actions);

            sendButton.Text = "发送";
            sendButton.Dock = DockStyle.Bottom;
            sendButton.Height = 42;
            StylePrimaryButton(sendButton);
            sendButton.Click += SendButtonClicked;
            actions.Controls.Add(sendButton);

            promptLibraryButton.Text = "提示词库";
            promptLibraryButton.Dock = DockStyle.Top;
            promptLibraryButton.Height = 38;
            StyleSecondaryButton(promptLibraryButton);
            promptLibraryButton.Click += ShowPromptLibrary;
            actions.Controls.Add(promptLibraryButton);
        }

        private void BuildSettingsPanel(Control parent)
        {
            Panel container = new Panel();
            container.Dock = DockStyle.Fill;
            container.AutoScroll = true;
            container.BackColor = Color.FromArgb(249, 250, 248);
            container.Padding = new Padding(16, 14, 16, 20);
            parent.Controls.Add(container);

            TableLayoutPanel fields = new TableLayoutPanel();
            fields.Dock = DockStyle.Top;
            fields.AutoSize = true;
            fields.ColumnCount = 1;
            fields.RowCount = 0;
            fields.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
            container.Controls.Add(fields);

            Label heading = new Label();
            heading.Text = "连接与参数";
            heading.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            heading.ForeColor = TextColor;
            heading.AutoSize = true;
            AddField(fields, heading, 42);

            AddLabeledControl(fields, "协议", protocolBox, 64);
            protocolBox.DropDownStyle = ComboBoxStyle.DropDownList;
            protocolBox.Items.AddRange(new object[] { "Chat Completions", "Responses API" });
            protocolBox.SelectedIndexChanged += delegate { UpdateRequestPreview(); };

            AddLabeledControl(fields, "Base URL", baseUrlBox, 64);
            baseUrlBox.TextChanged += delegate { UpdateRequestPreview(); };

            AddLabeledControl(fields, "API Key", apiKeyBox, 64);
            apiKeyBox.UseSystemPasswordChar = true;
            apiKeyBox.TextChanged += delegate { if (currentProvider != null) keys[currentProvider.Id] = apiKeyBox.Text; };

            Label modelLabel = CreateFieldLabel("模型");
            AddField(fields, modelLabel, 24);
            TableLayoutPanel modelRow = new TableLayoutPanel();
            modelRow.Dock = DockStyle.Fill;
            modelRow.ColumnCount = 2;
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            modelRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86F));
            modelBox.Dock = DockStyle.Fill;
            modelBox.DropDownStyle = ComboBoxStyle.DropDown;
            modelBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            modelBox.AutoCompleteSource = AutoCompleteSource.ListItems;
            modelBox.SelectedIndexChanged += delegate { ModelChanged(); };
            modelBox.TextChanged += delegate { ModelChanged(); };
            modelRow.Controls.Add(modelBox, 0, 0);
            syncModelsButton.Text = "同步模型";
            syncModelsButton.Dock = DockStyle.Fill;
            StyleSecondaryButton(syncModelsButton);
            syncModelsButton.Click += async delegate { await SyncModelsAsync(); };
            modelRow.Controls.Add(syncModelsButton, 1, 0);
            AddField(fields, modelRow, 38);

            TableLayoutPanel modelMeta = new TableLayoutPanel();
            modelMeta.Dock = DockStyle.Fill;
            modelMeta.ColumnCount = 2;
            modelMeta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55F));
            modelMeta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
            modelCountLabel.ForeColor = Muted;
            modelCountLabel.Dock = DockStyle.Fill;
            modelTypeLabel.ForeColor = Primary;
            modelTypeLabel.TextAlign = ContentAlignment.TopRight;
            modelTypeLabel.Dock = DockStyle.Fill;
            modelMeta.Controls.Add(modelCountLabel, 0, 0);
            modelMeta.Controls.Add(modelTypeLabel, 1, 0);
            AddField(fields, modelMeta, 28);

            systemPromptBox.Multiline = true;
            systemPromptBox.ScrollBars = ScrollBars.Vertical;
            systemPromptBox.Text = "你是一个专业、准确、简洁的 AI 助手。";
            systemPromptBox.TextChanged += delegate { UpdateRequestPreview(); };
            AddLabeledControl(fields, "System Prompt", systemPromptBox, 112);

            TableLayoutPanel numberRow = new TableLayoutPanel();
            numberRow.Dock = DockStyle.Fill;
            numberRow.ColumnCount = 2;
            numberRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            numberRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            numberRow.Controls.Add(BuildNumberField("Temperature", temperatureBox, 0, 2, 0.1M, 0.7M), 0, 0);
            numberRow.Controls.Add(BuildNumberField("Max Tokens", maxTokensBox, 1, 128000, 1M, 4096M), 1, 0);
            AddField(fields, numberRow, 70);

            AddLabeledControl(fields, "Reasoning", reasoningBox, 64);
            reasoningBox.DropDownStyle = ComboBoxStyle.DropDownList;
            reasoningBox.Items.AddRange(new object[] { "none", "low", "medium", "high", "xhigh", "max" });
            reasoningBox.SelectedItem = "medium";
            reasoningBox.SelectedIndexChanged += delegate { UpdateRequestPreview(); };

            streamCheck.Text = "流式输出";
            streamCheck.Checked = true;
            streamCheck.AutoSize = true;
            streamCheck.CheckedChanged += delegate { UpdateRequestPreview(); };
            AddField(fields, streamCheck, 38);

            customHeadersBox.Multiline = true;
            customHeadersBox.ScrollBars = ScrollBars.Vertical;
            customHeadersBox.TextChanged += delegate { UpdateRequestPreview(); };
            AddLabeledControl(fields, "自定义 Headers (JSON)", customHeadersBox, 94);

            Button clearButton = new Button();
            clearButton.Text = "清空当前会话";
            clearButton.Height = 38;
            clearButton.Dock = DockStyle.Fill;
            StyleSecondaryButton(clearButton);
            clearButton.Click += delegate { ClearConversation(); };
            AddField(fields, clearButton, 52);
        }

        private static Control BuildNumberField(string label, NumericUpDown control, decimal min, decimal max, decimal increment, decimal value)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            Label fieldLabel = CreateFieldLabel(label);
            fieldLabel.Dock = DockStyle.Top;
            fieldLabel.Height = 24;
            control.Minimum = min;
            control.Maximum = max;
            control.Increment = increment;
            control.DecimalPlaces = increment < 1 ? 1 : 0;
            control.Value = value;
            control.Dock = DockStyle.Top;
            control.Height = 30;
            panel.Controls.Add(control);
            panel.Controls.Add(fieldLabel);
            return panel;
        }

        private static void ConfigureInspectorBox(TextBox box)
        {
            box.Dock = DockStyle.Fill;
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Both;
            box.WordWrap = false;
            box.Font = new Font("Consolas", 9F);
            box.BackColor = Color.FromArgb(248, 249, 247);
        }

        private static Label CreateFieldLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.ForeColor = Muted;
            label.AutoSize = false;
            label.Dock = DockStyle.Top;
            label.TextAlign = ContentAlignment.BottomLeft;
            return label;
        }

        private static void AddLabeledControl(TableLayoutPanel table, string labelText, Control control, int totalHeight)
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            Label label = CreateFieldLabel(labelText);
            label.Height = 25;
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0);
            panel.Controls.Add(control);
            panel.Controls.Add(label);
            control.BringToFront();
            control.Top = 27;
            AddField(table, panel, totalHeight);
        }

        private static void AddField(TableLayoutPanel table, Control control, int height)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, 0, 0, 8);
            table.Controls.Add(control, 0, row);
        }

        private static void StylePrimaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = Primary;
            button.ForeColor = Color.White;
            button.Cursor = Cursors.Hand;
        }

        private static void StyleSecondaryButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(205, 212, 205);
            button.BackColor = Color.White;
            button.ForeColor = TextColor;
            button.Cursor = Cursors.Hand;
        }

        private void LoadProviders()
        {
            providerList.Items.Clear();
            foreach (Provider provider in providers) providerList.Items.Add(provider);
            int xelvIndex = providers.FindIndex(delegate(Provider p) { return p.Id == "xelvai"; });
            providerList.SelectedIndex = xelvIndex >= 0 ? xelvIndex : 0;
        }

        private void DrawProviderItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= providerList.Items.Count) return;
            Provider provider = providerList.Items[e.Index] as Provider;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using (Brush background = new SolidBrush(selected ? PrimarySoft : providerList.BackColor)) e.Graphics.FillRectangle(background, e.Bounds);
            using (Brush nameBrush = new SolidBrush(selected ? Primary : TextColor))
            using (Brush detailBrush = new SolidBrush(Muted))
            {
                e.Graphics.DrawString(provider.Name, new Font(Font, FontStyle.Bold), nameBrush, e.Bounds.Left + 10, e.Bounds.Top + 7);
                string detail = provider.Protocol == "responses" ? "Responses API" : "Chat Completions";
                e.Graphics.DrawString(detail, new Font(Font.FontFamily, 7.5F), detailBrush, e.Bounds.Left + 10, e.Bounds.Top + 27);
            }
        }

        private void ProviderChanged(object sender, EventArgs e)
        {
            Provider selected = providerList.SelectedItem as Provider;
            if (selected == null) return;
            if (currentProvider != null) keys[currentProvider.Id] = apiKeyBox.Text;
            currentProvider = selected;
            baseUrlBox.Text = selected.BaseUrl;
            protocolBox.SelectedIndex = selected.Protocol == "responses" ? 1 : 0;
            string key;
            apiKeyBox.Text = keys.TryGetValue(selected.Id, out key) ? key : "";
            SetModels(selected.Models.ToList(), false);
            SetStatus(selected.Name + " 已选择", false);
        }

        private void SetModels(List<string> models, bool synced)
        {
            string previous = modelBox.Text;
            modelBox.BeginUpdate();
            modelBox.Items.Clear();
            foreach (string model in models) modelBox.Items.Add(model);
            modelBox.EndUpdate();
            string next = models.Contains(previous) ? previous : (models.Contains("gpt-5.6") ? "gpt-5.6" : (models.Count > 0 ? models[0] : ""));
            modelBox.Text = next;
            modelCountLabel.Text = (synced ? "在线同步 " : "预设 ") + models.Count + " 个模型";
            ModelChanged();
        }

        private void ModelChanged()
        {
            string model = modelBox.Text;
            modelTypeLabel.Text = GetModelType(model);
            sendButton.Enabled = !IsImageModel(model) && sendCancellation == null;
            UpdateRequestPreview();
        }

        private static bool IsImageModel(string model)
        {
            return model.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0 || model.IndexOf("dall", StringComparison.OrdinalIgnoreCase) >= 0 || model.IndexOf("flux", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetModelType(string model)
        {
            if (IsImageModel(model)) return "图像模型";
            if (model.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0 || model.IndexOf("coder", StringComparison.OrdinalIgnoreCase) >= 0) return "代码模型";
            if (model.IndexOf("gpt-5", StringComparison.OrdinalIgnoreCase) >= 0 || model.IndexOf("reason", StringComparison.OrdinalIgnoreCase) >= 0) return "推理模型";
            return model.Length == 0 ? "未选择" : "文本模型";
        }

        private async Task SyncModelsAsync()
        {
            if (currentProvider == null) return;
            string baseUrl = baseUrlBox.Text.Trim().TrimEnd('/');
            if (baseUrl.Length == 0) { ShowError("请填写 Base URL。"); return; }
            if (currentProvider.RequiresKey && apiKeyBox.Text.Trim().Length == 0) { ShowError("请填写 API Key。"); return; }
            syncModelsButton.Enabled = false;
            syncModelsButton.Text = "同步中...";
            SetStatus("正在同步模型", true);
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, baseUrl + "/models"))
                {
                    ApplyHeaders(request);
                    using (HttpResponseMessage response = await Client.SendAsync(request))
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + json);
                        List<string> models = JsonTools.ParseModels(json);
                        if (models.Count == 0) throw new InvalidOperationException("接口没有返回可识别的模型列表。");
                        SetModels(models, true);
                        SetStatus("连接正常 · " + models.Count + " 个模型", false);
                    }
                }
            }
            catch (Exception exception)
            {
                SetStatus("模型同步失败", false);
                ShowError("模型同步失败：" + exception.Message);
            }
            finally
            {
                syncModelsButton.Enabled = true;
                syncModelsButton.Text = "同步模型";
            }
        }

        private void ApplyHeaders(HttpRequestMessage request)
        {
            string key = apiKeyBox.Text.Trim();
            if (key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            if (customHeadersBox.Text.Trim().Length > 0)
            {
                Dictionary<string, object> headers = serializer.Deserialize<Dictionary<string, object>>(customHeadersBox.Text);
                foreach (KeyValuePair<string, object> pair in headers) request.Headers.TryAddWithoutValidation(pair.Key, Convert.ToString(pair.Value));
            }
        }

        private void PromptKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SendButtonClicked(sender, EventArgs.Empty);
            }
        }

        private async void SendButtonClicked(object sender, EventArgs e)
        {
            if (sendCancellation != null) { sendCancellation.Cancel(); return; }
            await SendAsync();
        }

        private async Task SendAsync()
        {
            if (currentProvider == null) return;
            string prompt = promptBox.Text.Trim();
            if (prompt.Length == 0) return;
            if (baseUrlBox.Text.Trim().Length == 0 || modelBox.Text.Trim().Length == 0) { ShowError("请填写 Base URL 并选择模型。"); return; }
            if (currentProvider.RequiresKey && apiKeyBox.Text.Trim().Length == 0) { ShowError("请填写 API Key。"); return; }
            if (IsImageModel(modelBox.Text)) { ShowError("图像模型不适用于对话接口，请选择文本、推理或代码模型。"); return; }

            string protocol = protocolBox.SelectedIndex == 1 ? "responses" : "chat";
            string endpoint = baseUrlBox.Text.Trim().TrimEnd('/') + (protocol == "responses" ? "/responses" : "/chat/completions");
            Dictionary<string, object> body = BuildRequestBody(protocol, prompt);
            string requestJson = serializer.Serialize(body);
            requestBox.Text = PrettyJson(requestJson);
            rawBox.Clear();
            AppendChat("你", prompt, Color.FromArgb(65, 72, 66));
            messages.Add(new ChatMessage("user", prompt));
            promptBox.Clear();

            sendCancellation = new CancellationTokenSource();
            sendButton.Text = "停止";
            sendButton.Enabled = true;
            SetStatus("请求中", true);
            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint))
                {
                    ApplyHeaders(request);
                    request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    using (HttpResponseMessage response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, sendCancellation.Token))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            string failure = await response.Content.ReadAsStringAsync();
                            rawBox.Text = failure;
                            throw new InvalidOperationException("HTTP " + (int)response.StatusCode + ": " + failure);
                        }

                        string answer;
                        if (streamCheck.Checked) answer = await ReadStreamAsync(response, protocol, sendCancellation.Token);
                        else
                        {
                            string json = await response.Content.ReadAsStringAsync();
                            rawBox.Text = PrettyJson(json);
                            answer = JsonTools.ExtractText(json, protocol);
                            AppendChat(currentProvider.Name, answer.Length == 0 ? "请求完成，但响应中没有可显示的文本。" : answer, Primary);
                        }
                        if (answer.Length > 0) messages.Add(new ChatMessage("assistant", answer));
                        SetStatus("完成", false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("已停止", false);
            }
            catch (Exception exception)
            {
                SetStatus("请求失败", false);
                ShowError("请求失败：" + exception.Message);
            }
            finally
            {
                sendCancellation.Dispose();
                sendCancellation = null;
                sendButton.Text = "发送";
                sendButton.Enabled = !IsImageModel(modelBox.Text);
            }
        }

        private Dictionary<string, object> BuildRequestBody(string protocol, string newPrompt)
        {
            List<object> conversation = new List<object>();
            foreach (ChatMessage message in messages) conversation.Add(new Dictionary<string, object> { { "role", message.Role }, { "content", message.Content } });
            if (!String.IsNullOrWhiteSpace(newPrompt)) conversation.Add(new Dictionary<string, object> { { "role", "user" }, { "content", newPrompt } });
            if (protocol == "responses")
            {
                Dictionary<string, object> body = new Dictionary<string, object>();
                body["model"] = modelBox.Text.Trim();
                body["input"] = conversation.ToArray();
                body["instructions"] = systemPromptBox.Text;
                body["max_output_tokens"] = Decimal.ToInt32(maxTokensBox.Value);
                body["stream"] = streamCheck.Checked;
                body["reasoning"] = new Dictionary<string, object> { { "effort", Convert.ToString(reasoningBox.SelectedItem) } };
                if (Convert.ToString(reasoningBox.SelectedItem) == "none") body["temperature"] = temperatureBox.Value;
                return body;
            }
            List<object> chatMessages = new List<object>();
            if (systemPromptBox.Text.Trim().Length > 0) chatMessages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", systemPromptBox.Text } });
            chatMessages.AddRange(conversation);
            return new Dictionary<string, object>
            {
                { "model", modelBox.Text.Trim() },
                { "messages", chatMessages.ToArray() },
                { "temperature", temperatureBox.Value },
                { "max_tokens", Decimal.ToInt32(maxTokensBox.Value) },
                { "stream", streamCheck.Checked }
            };
        }

        private async Task<string> ReadStreamAsync(HttpResponseMessage response, string protocol, CancellationToken token)
        {
            StringBuilder full = new StringBuilder();
            AppendChatHeader(currentProvider.Name, Primary);
            using (Stream stream = await response.Content.ReadAsStreamAsync())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                while (!reader.EndOfStream)
                {
                    token.ThrowIfCancellationRequested();
                    string line = await reader.ReadLineAsync();
                    if (line == null) break;
                    rawBox.AppendText(line + Environment.NewLine);
                    if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                    string data = line.Substring(5).Trim();
                    if (data == "[DONE]" || data.Length == 0) continue;
                    try
                    {
                        string delta = JsonTools.ExtractStreamDelta(data, protocol);
                        if (delta.Length > 0)
                        {
                            full.Append(delta);
                            chatBox.SelectionColor = TextColor;
                            chatBox.AppendText(delta);
                            chatBox.ScrollToCaret();
                        }
                    }
                    catch { }
                }
            }
            chatBox.AppendText(Environment.NewLine + Environment.NewLine);
            return full.ToString();
        }

        private void AppendChat(string author, string content, Color authorColor)
        {
            AppendChatHeader(author, authorColor);
            chatBox.SelectionColor = TextColor;
            chatBox.SelectionFont = new Font(chatBox.Font, FontStyle.Regular);
            chatBox.AppendText(content + Environment.NewLine + Environment.NewLine);
            chatBox.ScrollToCaret();
        }

        private void AppendChatHeader(string author, Color color)
        {
            chatBox.SelectionColor = color;
            chatBox.SelectionFont = new Font(chatBox.Font, FontStyle.Bold);
            chatBox.AppendText(author + Environment.NewLine);
            chatBox.SelectionFont = new Font(chatBox.Font, FontStyle.Regular);
        }

        private void UpdateRequestPreview()
        {
            if (currentProvider == null || modelBox.Text.Trim().Length == 0) return;
            try
            {
                string protocol = protocolBox.SelectedIndex == 1 ? "responses" : "chat";
                Dictionary<string, object> body = BuildRequestBody(protocol, promptBox.Text.Trim());
                requestBox.Text = PrettyJson(serializer.Serialize(body));
            }
            catch { }
        }

        private void ShowPromptLibrary(object sender, EventArgs e)
        {
            using (PromptLibraryForm dialog = new PromptLibraryForm(templates))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    promptBox.Text = dialog.SelectedPrompt;
                    promptBox.Focus();
                    promptBox.SelectionStart = promptBox.TextLength;
                }
            }
        }

        private void ClearConversation()
        {
            if (sendCancellation != null) sendCancellation.Cancel();
            messages.Clear();
            chatBox.Clear();
            rawBox.Clear();
            requestBox.Clear();
            SetStatus("新会话", false);
        }

        private void SetStatus(string text, bool busy)
        {
            headerStatus.Text = busy ? "● " + text : text;
            headerStatus.ForeColor = busy ? Color.FromArgb(151, 102, 21) : Primary;
            statusLabel.Text = text;
        }

        private void ShowError(string message)
        {
            MessageBox.Show(this, message, "Relay AI API Studio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string PrettyJson(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) return json;
            StringBuilder output = new StringBuilder();
            bool quoted = false, escaped = false;
            int indent = 0;
            foreach (char c in json)
            {
                if (escaped) { output.Append(c); escaped = false; continue; }
                if (c == '\\' && quoted) { output.Append(c); escaped = true; continue; }
                if (c == '"') { quoted = !quoted; output.Append(c); continue; }
                if (quoted) { output.Append(c); continue; }
                if (c == '{' || c == '[') { output.Append(c).AppendLine(); indent++; output.Append(new string(' ', indent * 2)); }
                else if (c == '}' || c == ']') { output.AppendLine(); indent--; output.Append(new string(' ', indent * 2)).Append(c); }
                else if (c == ',') { output.Append(c).AppendLine(); output.Append(new string(' ', indent * 2)); }
                else if (c == ':') output.Append(": ");
                else if (!Char.IsWhiteSpace(c)) output.Append(c);
            }
            return output.ToString();
        }

        private static List<Provider> CreateProviders()
        {
            return new List<Provider>
            {
                new Provider("openai", "OpenAI", "https://api.openai.com/v1", "responses", true, "gpt-5.6", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.3-codex", "gpt-4.1-mini"),
                new Provider("xelvai", "Xelv AI", "https://api.xelvai.com/v1", "chat", true, "gpt-5.6", "gpt-5.6-sol", "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5", "gpt-5.4", "gpt-5.4-mini", "gpt-5.3-codex-spark", "gpt-5.2", "codex-auto-review", "gpt-image-2", "gpt-image-1.5", "gpt-image-1"),
                new Provider("openrouter", "OpenRouter", "https://openrouter.ai/api/v1", "chat", true, "openai/gpt-5.6", "anthropic/claude-sonnet-4.5", "google/gemini-2.5-pro"),
                new Provider("deepseek", "DeepSeek", "https://api.deepseek.com", "chat", true, "deepseek-chat", "deepseek-reasoner"),
                new Provider("qwen", "通义千问", "https://dashscope.aliyuncs.com/compatible-mode/v1", "chat", true, "qwen3-max", "qwen-plus", "qwen3-coder-plus"),
                new Provider("siliconflow", "硅基流动", "https://api.siliconflow.cn/v1", "chat", true, "deepseek-ai/DeepSeek-V3", "Qwen/Qwen3-30B-A3B"),
                new Provider("moonshot", "Moonshot", "https://api.moonshot.cn/v1", "chat", true, "kimi-k2-0711-preview", "moonshot-v1-32k"),
                new Provider("xai", "xAI", "https://api.x.ai/v1", "chat", true, "grok-4", "grok-4-fast", "grok-3-mini"),
                new Provider("groq", "Groq", "https://api.groq.com/openai/v1", "chat", true, "openai/gpt-oss-120b", "llama-3.3-70b-versatile"),
                new Provider("mistral", "Mistral", "https://api.mistral.ai/v1", "chat", true, "mistral-large-latest", "codestral-latest"),
                new Provider("together", "Together AI", "https://api.together.xyz/v1", "chat", true, "deepseek-ai/DeepSeek-V3"),
                new Provider("perplexity", "Perplexity", "https://api.perplexity.ai", "chat", true, "sonar-pro", "sonar", "sonar-reasoning-pro"),
                new Provider("ollama", "Ollama 本地", "http://localhost:11434/v1", "chat", false),
                new Provider("lmstudio", "LM Studio", "http://localhost:1234/v1", "chat", false),
                new Provider("custom", "自定义接口", "", "chat", true)
            };
        }

        private static List<PromptTemplate> CreateTemplates()
        {
            return new List<PromptTemplate>
            {
                new PromptTemplate("编程", "审查代码", "检查正确性、安全性、性能与可维护性", "请审查下面的代码。优先找出会导致错误、数据丢失、安全问题或性能退化的地方，然后按严重程度列出问题并给出最小修改方案。最后补充缺失的测试用例。\r\n\r\n代码：\r\n"),
                new PromptTemplate("编程", "分析报错", "根据错误信息定位根因并给出验证步骤", "请分析下面的报错。先判断最可能的根因，再说明证据、排查顺序和最小修复方案。请给出可以直接执行的验证步骤。\r\n\r\n报错与相关代码：\r\n"),
                new PromptTemplate("编程", "重构方案", "在保留行为的前提下降低复杂度", "请在不改变外部行为的前提下重构下面的代码。指出复杂度和耦合来源，提供完整代码，并说明如何验证新旧行为一致。\r\n\r\n代码：\r\n"),
                new PromptTemplate("编程", "生成测试", "覆盖正常流程、边界条件与失败路径", "请为下面的代码编写测试，覆盖正常流程、边界条件、无效输入、异常处理和回归风险。\r\n\r\n代码：\r\n"),
                new PromptTemplate("产品", "设计 API", "输出资源模型、接口、错误码与示例", "请为下面的需求设计可落地的 API。包含资源模型、端点、请求响应示例、分页、鉴权、幂等、错误码、限流和版本兼容策略。\r\n\r\n需求：\r\n"),
                new PromptTemplate("产品", "产品需求文档", "整理目标、范围、流程与验收标准", "请把下面的想法整理成产品需求文档，包含目标用户、核心问题、范围与非范围、关键流程、异常状态、数据指标和验收标准。\r\n\r\n想法：\r\n"),
                new PromptTemplate("分析", "方案对比", "用一致标准比较多个方案", "请比较下面的方案。建立统一评价维度，从成本、复杂度、性能、风险、可维护性和适用场景分析，并给出明确建议。\r\n\r\n方案：\r\n"),
                new PromptTemplate("分析", "提取结构化数据", "将文本整理成稳定 JSON", "请从下面内容提取结构化信息并仅输出有效 JSON。字段缺失时使用 null，不要猜测，保留原始单位和专有名词。\r\n\r\n内容：\r\n"),
                new PromptTemplate("分析", "提炼摘要", "提取结论、依据、风险与行动项", "请总结下面内容，提取核心结论、关键依据、不确定点、风险和下一步行动项。数字、日期和专有名词必须保留。\r\n\r\n内容：\r\n"),
                new PromptTemplate("写作", "润色文本", "提升清晰度并保留原意和语气", "请润色下面文本，使表达自然、清晰、具体。保留原意和事实，删除空话与重复，不添加未经提供的信息。\r\n\r\n原文：\r\n"),
                new PromptTemplate("写作", "撰写邮件", "生成清楚、得体且有行动指向的邮件", "请根据下面信息写一封邮件。主题明确，正文交代必要背景和行动项，结尾给出期望回复时间，语气专业自然。\r\n\r\n信息：\r\n"),
                new PromptTemplate("写作", "专业翻译", "保留术语、格式与语义层次", "请将下面内容翻译成目标语言。保留标题、列表、代码、变量名和专有名词，术语前后一致。\r\n\r\n目标语言：\r\n内容：\r\n"),
                new PromptTemplate("学习", "深入解释概念", "从直觉、原理到实例逐层说明", "请解释下面概念。先用准确的直觉类比，再解释核心原理、适用条件、常见误区和实际例子，最后给出三个理解检查问题。\r\n\r\n概念：\r\n"),
                new PromptTemplate("学习", "制定学习计划", "按目标和时间生成可执行路径", "请为下面目标制定可执行学习计划，列出前置知识、阶段主题、练习项目、验证标准和复盘节点。\r\n\r\n学习目标：\r\n现有基础：\r\n可用时间：\r\n"),
                new PromptTemplate("创意", "结构化头脑风暴", "产生差异明显且可评估的想法", "请围绕下面主题进行结构化头脑风暴。先明确约束，再从五个不同方向提出想法，每个包含机制、用户、优势、风险和验证方式。\r\n\r\n主题与约束：\r\n"),
                new PromptTemplate("创意", "优化提示词", "提高任务边界、输出约束和可验证性", "请优化下面提示词。保留目标，补充上下文、成功标准、约束、输出格式和失败处理规则，并删除重复或冲突要求。\r\n\r\n原提示词：\r\n")
            };
        }

        public static bool SelfTest()
        {
            try
            {
                List<string> models = JsonTools.ParseModels("{\"data\":[{\"id\":\"gpt-5.6\"},{\"id\":\"gpt-5.6-luna\"}]}");
                string text = JsonTools.ExtractText("{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}", "chat");
                string delta = JsonTools.ExtractStreamDelta("{\"choices\":[{\"delta\":{\"content\":\"x\"}}]}", "chat");
                return models.Count == 2 && models[0] == "gpt-5.6" && text == "ok" && delta == "x";
            }
            catch { return false; }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test") return MainForm.SelfTest() ? 0 : 1;
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 1 && args[0] == "--render-test")
            {
                try
                {
                    using (MainForm preview = new MainForm())
                    {
                        preview.StartPosition = FormStartPosition.Manual;
                        preview.Location = new Point(-10000, -10000);
                        preview.ShowInTaskbar = false;
                        preview.Show();
                        Application.DoEvents();
                        using (Bitmap bitmap = new Bitmap(preview.Width, preview.Height))
                        {
                            preview.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                            bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                        }
                        preview.Close();
                    }
                    return 0;
                }
                catch (Exception exception)
                {
                    string detail = exception.GetType().FullName + " | " + exception.Message + Environment.NewLine + exception.StackTrace;
                    if (exception.InnerException != null) detail += " | " + exception.InnerException.GetType().FullName + " | " + exception.InnerException.Message;
                    File.WriteAllText(args[1] + ".error.txt", detail, Encoding.UTF8);
                    return 2;
                }
            }
            Application.Run(new MainForm());
            return 0;
        }
    }
}
