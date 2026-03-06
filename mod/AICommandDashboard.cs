using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
using System.Linq;

namespace SRB2AIControl
{
    public class CommandDashboard : Form
    {
        private FlowLayoutPanel panelCommands;
        private Panel panelCreator;
        private FlowLayoutPanel gridItems;
        private Label lblStatus;
        private Label lblPreview;
        private TextBox txtSearch;
        private CheckBox chkModeSwitch;
        
        private List<SRB2Item> allItems = new List<SRB2Item>();

        public CommandDashboard()
        {
            this.Text = "SRB2 AI MANUAL OVERRIDE V3.0 - OFFICIAL DESIGN";
            this.Size = new Size(650, 900);
            this.BackColor = Color.FromArgb(25, 25, 30);
            this.ForeColor = Color.White;
            this.Font = new Font("Segoe UI", 10F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;

            InitializeItems();
            InitializeUI();
        }

        private void InitializeItems()
        {
            allItems.Add(new SRB2Item("Anillo", 107, Color.Gold, "Anillo basico."));
            allItems.Add(new SRB2Item("Moneda Mario", 558, Color.Yellow, "Moneda estilo Mario."));
            allItems.Add(new SRB2Item("Caja de Anillos", 157, Color.Gold, "Otorga anillos."));
            allItems.Add(new SRB2Item("Caja Vida Extra", 166, Color.DeepSkyBlue, "Otorga una vida."));
            allItems.Add(new SRB2Item("Cristal Azul", 261, Color.Cyan, "Bloque solido y rompible."));
            allItems.Add(new SRB2Item("Gárgola", 251, Color.Gray, "Estatua solida de piedra."));
            allItems.Add(new SRB2Item("Árbol GFZ", 234, Color.ForestGreen, "Arbol tipico de GFZ."));
            allItems.Add(new SRB2Item("Arbolito (B Arbusto)", 233, Color.Green, "Arbusto verde decorativo."));
            allItems.Add(new SRB2Item("Arbusto Moras", 232, Color.DarkRed, "Arbusto con moras rojas."));
            allItems.Add(new SRB2Item("Palmera", 459, Color.SandyBrown, "Palmera de playa."));
            allItems.Add(new SRB2Item("Pino", 295, Color.DarkGreen, "Pino estilo CEZ."));
            allItems.Add(new SRB2Item("Cáctus Grande", 319, Color.OliveDrab, "Cactus del desierto."));
            allItems.Add(new SRB2Item("Cáctus Bola", 320, Color.Olive, "Cactus redondo."));
            allItems.Add(new SRB2Item("Muñeco Nieve", 384, Color.White, "Muñeco de nieve."));
            allItems.Add(new SRB2Item("Calabaza", 401, Color.Orange, "Calabaza de Halloween."));
            allItems.Add(new SRB2Item("Goomba", 560, Color.SaddleBrown, "Enemigo de Mario."));
            allItems.Add(new SRB2Item("Koopa", 569, Color.LimeGreen, "Tortuga de Mario."));
            allItems.Add(new SRB2Item("Crawla Azul", 6, Color.Blue, "Enemigo basico azul."));
            allItems.Add(new SRB2Item("Crawla Rojo", 7, Color.Red, "Enemigo basico rojo."));
            allItems.Add(new SRB2Item("Flicky Bluebird", 477, Color.LightBlue, "Animalito rescatado."));
            allItems.Add(new SRB2Item("Estalagmita", 372, Color.DimGray, "Piedra de cueva."));
            allItems.Add(new SRB2Item("Antorcha", 301, Color.OrangeRed, "Fuente de luz."));
            allItems.Add(new SRB2Item("Vela", 298, Color.Bisque, "Vela pequeña."));
            allItems.Add(new SRB2Item("Estatua Eggman", 461, Color.Red, "Decoracion malvada."));
            allItems.Add(new SRB2Item("Mina Flotante", 216, Color.Black, "Peligro explosivo."));
            allItems.Add(new SRB2Item("Muelle Amarillo", 132, Color.Yellow, "Salto medio."));
            allItems.Add(new SRB2Item("Muelle Rojo", 133, Color.Red, "Salto alto."));
            allItems.Add(new SRB2Item("Cilindro de Pasto", 501, Color.LimeGreen, "Plataforma cilíndrica de pasto."));
            allItems.Add(new SRB2Item("Minecraft Block", 660, Color.SaddleBrown, "Solid breakable block"));
            
            for (int i = 312; i <= 318; i++) allItems.Add(new SRB2Item("Cáctus Var " + (i-311), i, Color.Green, "Variedad de cactus."));
            for (int i = 373; i <= 381; i++) allItems.Add(new SRB2Item("Estalagmita Var " + (i-371), i, Color.Gray, "Variedad de roca."));
            for (int i = 228; i <= 231; i++) allItems.Add(new SRB2Item("Flor GFZ " + (i-227), i, Color.Pink, "Flor decorativa."));
        }

        private void InitializeUI()
        {
            // Panel Superior (Header)
            Panel header = new Panel();
            header.Dock = DockStyle.Top;
            header.Height = 130;
            header.BackColor = Color.FromArgb(30,30,35);
            this.Controls.Add(header);

            Label lblTitle = new Label();
            lblTitle.Text = "SRB2 AI MANUAL OVERRIDE";
            lblTitle.Font = new Font("Segoe UI", 16F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 160, 255);
            lblTitle.Location = new Point(20, 15);
            lblTitle.Size = new Size(400, 35);
            header.Controls.Add(lblTitle);

            lblStatus = new Label();
            lblStatus.Text = "Esperando comandos...";
            lblStatus.ForeColor = Color.Gray;
            lblStatus.Location = new Point(25, 55);
            lblStatus.Size = new Size(500, 20);
            header.Controls.Add(lblStatus);

            // Toggle Switch
            chkModeSwitch = new CheckBox();
            chkModeSwitch.Text = "HABILITAR MODO CREADOR DE ITEMS";
            chkModeSwitch.Appearance = Appearance.Button;
            chkModeSwitch.TextAlign = ContentAlignment.MiddleCenter;
            chkModeSwitch.FlatStyle = FlatStyle.Flat;
            chkModeSwitch.BackColor = Color.FromArgb(50, 50, 55);
            chkModeSwitch.FlatAppearance.CheckedBackColor = Color.FromArgb(0, 120, 215);
            chkModeSwitch.Location = new Point(20, 85);
            chkModeSwitch.Size = new Size(300, 35);
            chkModeSwitch.CheckedChanged += (s, e) => ToggleCreatorMode();
            header.Controls.Add(chkModeSwitch);

            txtSearch = new TextBox();
            txtSearch.Visible = false;
            txtSearch.Location = new Point(330, 90);
            txtSearch.Size = new Size(200, 25);
            txtSearch.BackColor = Color.FromArgb(40, 40, 45);
            txtSearch.ForeColor = Color.White;
            txtSearch.TextChanged += (s, e) => FilterItems(txtSearch.Text);
            header.Controls.Add(txtSearch);

            // Panel de Contenido Principal
            Panel contentContainer = new Panel();
            contentContainer.Dock = DockStyle.Fill;
            this.Controls.Add(contentContainer);

            // 1. Panel de Comandos Originales
            panelCommands = new FlowLayoutPanel();
            panelCommands.Dock = DockStyle.Fill;
            panelCommands.AutoScroll = true;
            panelCommands.FlowDirection = FlowDirection.TopDown;
            panelCommands.WrapContents = false;
            panelCommands.Padding = new Padding(20, 30, 20, 20);
            contentContainer.Controls.Add(panelCommands);

            // 2. Panel del Creador (con Grid y Preview)
            panelCreator = new Panel();
            panelCreator.Dock = DockStyle.Fill;
            panelCreator.Visible = false;
            contentContainer.Controls.Add(panelCreator);

            gridItems = new FlowLayoutPanel();
            gridItems.Dock = DockStyle.Fill;
            gridItems.AutoScroll = true;
            gridItems.Padding = new Padding(10, 30, 10, 10);
            panelCreator.Controls.Add(gridItems);

            Panel previewPanel = new Panel();
            previewPanel.Dock = DockStyle.Bottom;
            previewPanel.Height = 60;
            previewPanel.BackColor = Color.FromArgb(20, 20, 25);
            panelCreator.Controls.Add(previewPanel);

            lblPreview = new Label();
            lblPreview.Dock = DockStyle.Fill;
            lblPreview.TextAlign = ContentAlignment.MiddleLeft;
            lblPreview.Padding = new Padding(20, 0, 0, 0);
            lblPreview.Text = "Pasa el mouse por un objeto para ver info.";
            lblPreview.ForeColor = Color.Cyan;
            previewPanel.Controls.Add(lblPreview);

            PopulateOriginalCommands();
            PopulateItemGrid(allItems);
            
            // Fix Overlap: Header must be at the BACK of Z-order to claim Dock space first.
            header.SendToBack(); 
            contentContainer.BringToFront();
        }

        private void ToggleCreatorMode()
        {
            bool active = chkModeSwitch.Checked;
            panelCommands.Visible = !active;
            panelCreator.Visible = active;
            txtSearch.Visible = active;
            
            if (active) {
                lblStatus.Text = "Modo: Creador de Items Activo";
            } else {
                lblStatus.Text = "Modo: Comandos Manuales Activo";
            }
        }

        private void PopulateOriginalCommands()
        {
            panelCommands.Controls.Clear();
            AddCommandRow("GESTION DE VIDA", "Añade una vida o la quita.", "addlives 1", "sublives 1", "Añadir Vida", "Quitar Vida");
            AddCommandRow("GESTION DE ANILLOS", "Suma anillos o los resetea a cero.", "addrings 50", "addrings -999", "Sumar 50", "Reset (0)");
            AddCommandRow("ESCUDO GIGANTE", "Aumenta el tamaño (+20%) o lo resetea.", "ai_scale 1.2", "ai_reset_scale", "Crecer", "Normal");
            AddCommandRow("ESCUDO MINIMO", "Reduce el tamaño (-20%) o lo resetea.", "ai_scale 0.8", "ai_reset_scale", "Encoger", "Normal");
            AddCommandRow("GRAVEDAD LUNAR", "Establece gravedad lunar (0.3) o normal (0.5).", "ai_gravity 0.3", "ai_gravity 0.5", "Lunar", "Normal");
            AddCommandRow("GRAVEDAD PESADA", "Establece gravedad alta (4.0) o normal (0.5).", "ai_gravity 4.0", "ai_gravity 0.5", "Pesada", "Normal");
            AddCommandRow("MODO DIOS", "Alterna la invencibilidad.", "ai_god", "", "Alternar", "");
            AddCommandRow("SALTO FORZADO", "Fuerza un salto instantáneo.", "ai_force_jump", "", "SALTAR!", "--");
            AddCommandRow("DAÑAR JUGADOR", "El jugador pierde anillos.", "ai_hurt", "", "DAÑAR!", "");
            AddCommandRow("ELIMINAR JUGADOR", "Mata al jugador.", "ai_kill", "", "MATAR!", "");
            AddCommandRow("MULTIPLICAR ENEMIGOS", "Duplica los enemigos cercanos.", "ai_multiply", "", "MULTIPLICAR!", "");
            AddCommandRow("SPAWN RAPIDO", "Crea objetos predefinidos.", "ai_spawn_block", "ai_spawn_block_mc", "GARGOLA", "MINECRAFT");
        }

        private void PopulateItemGrid(IEnumerable<SRB2Item> items)
        {
            gridItems.Controls.Clear();
            foreach (var item in items)
            {
                Panel card = new Panel();
                card.Size = new Size(110, 140);
                card.Margin = new Padding(5);
                card.BackColor = Color.FromArgb(40, 40, 45);
                card.BorderStyle = BorderStyle.FixedSingle;

                Panel icon = new Panel();
                icon.Size = new Size(60, 60);
                icon.Location = new Point(25, 12);
                icon.BackColor = item.Color;
                icon.BorderStyle = BorderStyle.Fixed3D;
                icon.Click += (s, e) => SendCommand("ai_spawn " + item.ID);
                icon.MouseEnter += (s, e) => { card.BackColor = Color.FromArgb(60, 60, 70); lblPreview.Text = item.Name + ": " + item.Desc; };
                icon.MouseLeave += (s, e) => card.BackColor = Color.FromArgb(40, 40, 45);
                card.Controls.Add(icon);

                Label lbl = new Label();
                lbl.Text = item.Name;
                lbl.Font = new Font("Segoe UI", 7F, FontStyle.Bold);
                lbl.TextAlign = ContentAlignment.TopCenter;
                lbl.Location = new Point(5, 80);
                lbl.Size = new Size(100, 50);
                lbl.ForeColor = Color.White;
                lbl.Click += (s, e) => SendCommand("ai_spawn " + item.ID);
                lbl.MouseEnter += (s, e) => { card.BackColor = Color.FromArgb(60, 60, 70); lblPreview.Text = item.Name + ": " + item.Desc; };
                card.Controls.Add(lbl);

                card.Click += (s, e) => SendCommand("ai_spawn " + item.ID);
                gridItems.Controls.Add(card);
            }
        }

        private void FilterItems(string q)
        {
            PopulateItemGrid(allItems.Where(i => i.Name.ToLower().Contains(q.ToLower())));
        }

        private void AddCommandRow(string title, string desc, string cmd1, string cmd2, string lbl1, string lbl2)
        {
            Panel row = new Panel();
            row.Size = new Size(550, 100);
            row.BackColor = Color.FromArgb(40, 40, 45);
            row.Margin = new Padding(0, 0, 0, 10);

            Label l1 = new Label(); l1.Text = title; l1.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            l1.Location = new Point(15, 10); row.Controls.Add(l1);

            Label l2 = new Label(); l2.Text = desc; l2.Font = new Font("Segoe UI", 8F);
            l2.ForeColor = Color.Silver; l2.Location = new Point(15, 30); l2.Size = new Size(500, 20);
            row.Controls.Add(l2);

            Button b1 = new Button(); b1.Text = lbl1; b1.Location = new Point(15, 55);
            b1.Size = new Size(160, 32); b1.BackColor = Color.FromArgb(0, 120, 215);
            b1.FlatStyle = FlatStyle.Flat; b1.Click += (s, e) => SendCommand(cmd1);
            row.Controls.Add(b1);

            if (!string.IsNullOrEmpty(lbl2)) {
                Button b2 = new Button(); b2.Text = lbl2; b2.Location = new Point(185, 55);
                b2.Size = new Size(160, 32); b2.BackColor = Color.FromArgb(200, 40, 40);
                b2.FlatStyle = FlatStyle.Flat; b2.Click += (s, e) => SendCommand(cmd2);
                row.Controls.Add(b2);
            }

            panelCommands.Controls.Add(row);
        }

        private void SendCommand(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            System.Threading.ThreadPool.QueueUserWorkItem((_) => {
                try {
                    using (TcpClient client = new TcpClient("127.0.0.1", 1235))
                    using (NetworkStream stream = client.GetStream()) {
                        byte[] data = Encoding.UTF8.GetBytes(cmd + "\n");
                        stream.Write(data, 0, data.Length);
                        this.Invoke(new Action(() => { lblStatus.Text = "Ejecutado: " + cmd; lblStatus.ForeColor = Color.Cyan; }));
                    }
                } catch {
                    this.Invoke(new Action(() => { lblStatus.Text = "Error de conexion (¿SRB2 ejecutandose?)"; lblStatus.ForeColor = Color.Tomato; }));
                }
            });
        }

        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CommandDashboard());
        }
    }

    public class SRB2Item
    {
        public string Name { get; set; }
        public int ID { get; set; }
        public Color Color { get; set; }
        public string Desc { get; set; }
        public SRB2Item(string n, int i, Color c, string d) { Name = n; ID = i; Color = c; Desc = d; }
    }
}
