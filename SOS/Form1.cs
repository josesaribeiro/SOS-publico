﻿using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SOS
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            this.Load += Form1_Load;
        }
        private string LogText = "";

        private void UILogUpdate(string report)
        {
            LogText += $"{DateTime.Now.ToString("MM/dd/yyyy HH:mm:ss")}: {report}{Environment.NewLine}";
            report = report.Length > 100 ? $"{report.Substring(0, 100)}..." : report;
            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)(() =>
                {
                    toolStripStatusLabel1.Text = $"Atualização de documentos: {report}";
                }));
            }
        }
        private void FileLogUpdate()
        {
            File.WriteAllText($"{Environment.CurrentDirectory}/Documentos/log.txt", LogText);
        }

        private void Form1_Load(object sender, EventArgs e)
        {

            Task.Run(() =>
            {
                //UpdateMPO();
                //FileLogUpdate();
                UpdateDiagramasONS();
                FileLogUpdate();

            });
        }

        private void UpdateDiagramasONS()
        {
            UILogUpdate("#Início - Diagramas ONS");
            UILogUpdate("cdre.org.br/ conectando...");
            List<Row> diagramasONS = WebScrap.ScrapOnsDiagramasAsync("fsaolive", "123123aA").GetAwaiter().GetResult();

            FileInfo jsonInfoFile = new FileInfo($"{Environment.CurrentDirectory}/Documentos/Diagramas/info.json");
            if (!jsonInfoFile.Directory.Exists) Directory.CreateDirectory(jsonInfoFile.Directory.FullName);

            string jsonInfo = File.Exists(jsonInfoFile.FullName) ? File.ReadAllText(jsonInfoFile.FullName) : null; 
            List<Row> localDiagramas = string.IsNullOrEmpty(jsonInfo) ? new List<Row>() : JsonConvert.DeserializeObject<List<Row>>(jsonInfo);

            var client = new WebScrap.CookieAwareWebClient();

            foreach (var diagrama in diagramasONS)
            {
                UILogUpdate($"{diagrama.FileLeafRef} atualizando");
                string diagramaLink = $"https://cdre.ons.org.br{diagrama.FileRef}";

                FileInfo diagramaFile = new FileInfo($"{Environment.CurrentDirectory}/Documentos/Diagramas/{diagrama.FileLeafRef.Split('_').First()}.pdf");
                
                string revisao = localDiagramas.Where(w=>w.FileLeafRef.Split('_').First() == diagrama.FileLeafRef.Split('_').First()).Select(s=>s.Modified).FirstOrDefault();
                if (revisao == diagrama.Modified)
                {
                    UILogUpdate($"{diagrama.FileLeafRef} já se encontra na revisão vigente");
                    continue;
                }
                try
                {
                    client.DownloadFile(diagramaLink, diagramaFile.FullName);
                    UILogUpdate($"{diagrama.FileLeafRef.Split('_').First()} atualizado da modificação {revisao} para modificação {diagrama.Modified} em {diagramaFile.FullName}");
                }
                catch (Exception)
                {
                    UILogUpdate($"{diagrama.FileLeafRef.Split('_').First()} não foi possível atualização pelo link {diagramaLink}");
                }
            }
            string json = JsonConvert.SerializeObject(diagramasONS);
            File.WriteAllText(jsonInfoFile.FullName, json);
            client.Dispose();
            UILogUpdate("#Término - Diagramas ONS");
        }

        private void UpdateMPO()
        {
            UILogUpdate("#Início - Procedimentos da Operação (MPO)");
            UILogUpdate("ons.org.br/ conectando...");
            List<ChildItem> docsMPO = WebScrap.ScrapMPOAsync("FURNAS").GetAwaiter().GetResult();
            var client = new WebClient();
            foreach (var doc in docsMPO)
            {
                UILogUpdate($"{doc.MpoCodigo} atualizando");
                string docLink = $"http://ons.org.br{doc.FileRef}";
                FileInfo docFile = new FileInfo($"{Environment.CurrentDirectory}/Documentos/MPO/{doc.MpoCodigo}/{doc.MpoCodigo}.pdf");
                if (!docFile.Directory.Exists) Directory.CreateDirectory(docFile.Directory.FullName);

                string revisao = File.Exists($"{docFile.DirectoryName}/info.cfg") ? File.ReadAllText($"{docFile.DirectoryName}/info.cfg") : null;
                if (revisao == doc._Revision)
                {
                    UILogUpdate($"{doc.MpoCodigo} já se encontra na revisão vigente");
                    continue;
                }
                try
                {
                    client.DownloadFile(docLink, docFile.FullName);
                    UILogUpdate($"{doc.MpoCodigo} atualizado da revisão {revisao} para revisão {doc._Revision} em {docFile.FullName}");
                    File.WriteAllText($"{docFile.DirectoryName}/info.cfg", doc._Revision);
                    if (doc.MpoAlteradosPelasMops != null)
                    {
                        File.WriteAllText($"{docFile.DirectoryName}/mop.cfg", doc.MpoAlteradosPelasMops.Replace('/', '-'));
                        UILogUpdate($"{doc.MpoCodigo} está sendo alterado por {doc.MpoAlteradosPelasMops.Replace('/', '-')}");
                    }
                }
                catch (Exception)
                {
                    UILogUpdate($"{doc.MpoCodigo} não foi possível atualização pelo link {docLink}");
                }
            }
            List<string> mopLinks = docsMPO.Where(w => w.MpoMopsLink != null).SelectMany(s => s.MpoMopsLink).Distinct().ToList();
            string mopDir = $"{Environment.CurrentDirectory}/Documentos/MPO/MOP";
            if (!Directory.Exists(mopDir)) Directory.CreateDirectory(mopDir);
            var mopsLocal = Directory.GetFiles(mopDir, "*.pdf").ToList();
            var mopsVigentes = mopLinks.Select(s => $"{mopDir}\\{s.Split('/').Last()}").ToList();
            mopsLocal = mopsLocal.Where(w=>!mopsVigentes.Contains(w)).ToList();
            mopsLocal.ForEach(s => 
            {
                File.Delete(s);
                UILogUpdate($"{s.Split('/').Last()} não está vigente e foi apagada");
            });
            foreach (var mopLink in mopLinks)
            {
                FileInfo mopFile = new FileInfo($"{Environment.CurrentDirectory}/Documentos/MPO/MOP/{mopLink.Split('/').Last()}");
                UILogUpdate($"{mopFile.Name} atualizando");
                if (mopFile.Exists)
                {
                    UILogUpdate($"{mopFile.Name} já está disponível em {mopFile.FullName}");
                    continue;
                }
                try
                {
                    client.DownloadFile(mopLink, mopFile.FullName);
                    UILogUpdate($"{mopFile.Name} disponível em {mopFile.FullName}");
                }
                catch (Exception)
                {
                    UILogUpdate($"{mopFile.Name} não foi possível atualização pelo link {mopLink}");
                }
            }
            client.Dispose();
            UILogUpdate("#Término - Procedimentos da Operação (MPO)");
        }
    }
}
