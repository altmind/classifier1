using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.OleDb;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using FANN.Net;

namespace classifier1
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                if (saveFileDialog1.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        processFileConversion(openFileDialog1.FileName, saveFileDialog1.FileName);
                    }
                    catch (Exception ex)
                    {

                        MessageBox.Show(ex.ToString());
                    }
                }
            }
        }

        private void processFileConversion(string inFileName, string outFileName)
        {
            backgroundWorker1.RunWorkerAsync(new Tuple<string, string>(inFileName, outFileName));
        }

        private void backgroundWorker1_DoWork(object sender, DoWorkEventArgs e)
        {
            words2pos = new Dictionary<string, long>();
            wordsOrder = new List<string>();
            outputValues = new Dictionary<string, long>();
            totalRows = 0;
            Invoke(new MethodInvoker(
                 delegate
                 {
                     button1.Enabled = false;
                     tabControl1.Enabled = false;
                 }
                 ));

            Tuple<string, string> arg = (Tuple<string, string>)e.Argument;

            OleDbConnection myOleDbConnection = null;
            OleDbCommand myOleDbCommand = null;
            OleDbDataReader myOleDbDataReader = null;
            StreamWriter outputFile = null;
            StreamWriter dictOutputFile = null;
            StreamWriter catOutputFile = null;

            try
            {
                string connectionString = "provider=Microsoft.Jet.OLEDB.4.0;data source=" + arg.Item1;
                myOleDbConnection = new OleDbConnection(connectionString);
                myOleDbCommand = myOleDbConnection.CreateCommand();

                // TOP 100
                myOleDbCommand.CommandText = "SELECT TOP " + (int)numericUpDown3.Value + " Recept, Content, ReceptDescription,DishType FROM tblMain";
                myOleDbConnection.Open();
                myOleDbDataReader = myOleDbCommand.ExecuteReader();
                int i = 0;
                while (myOleDbDataReader.Read())
                {
                    i++;
                    prepareTrainingData((string)myOleDbDataReader["Content"], (string)myOleDbDataReader["DishType"], 1, null);
                    if (i % 541 == 0)
                    {
                        report("First pass: " + i);
                    }
                }
                myOleDbDataReader.Close();

                myOleDbCommand.CommandText = "SELECT TOP " + (int)numericUpDown3.Value + " Recept, Content, ReceptDescription,DishType FROM tblMain";
                myOleDbDataReader = myOleDbCommand.ExecuteReader();
                outputFile = new StreamWriter(File.OpenWrite(arg.Item2));
                outputFile.WriteLine(totalRows + " " + wordsOrder.Count + " 1");
                i = 0;
                while (myOleDbDataReader.Read())
                {
                    i++;
                    prepareTrainingData((string)myOleDbDataReader["Content"], (string)myOleDbDataReader["DishType"], 2, outputFile);
                    if (i % 541 == 0)
                    {
                        report("Second pass: " + i);
                    }
                }
                report("Dict and cat dump");
                dictOutputFile = new StreamWriter(File.OpenWrite(arg.Item2 + ".words.dict"));
                foreach (string word in wordsOrder)
                {
                    dictOutputFile.WriteLine(word);
                }
                catOutputFile = new StreamWriter(File.OpenWrite(arg.Item2 + ".words.cat"));
                foreach (string val in outputValues.OrderBy(x => x.Value).Select(x => x.Key))
                {
                    catOutputFile.WriteLine(val);
                }

                report("Creating network");
                NeuralNet net = new NeuralNet();
                net.SetActivationFunctionHidden(ActivationFunction.SigmoidSymmetric);
                net.Callback += new NeuralNet.CallbackType(fannProgress);
                uint[] layers = textBox5.Text.Split(new char[] { ',' }).Select(x => UInt32.Parse(x.Trim())).ToArray();
                net.CreateStandardArray(layers);

                TrainingData data = new TrainingData();
                outputFile.Close();
                report("Reading data");
                data.ReadTrainFromFile(arg.Item2);
                report("Doing training");
                net.TrainOnData(data, (uint)numericUpDown1.Value, 10, (float)numericUpDown2.Value);

                net.Save(arg.Item2 + ".ann");
                report("Done training. Saved.");
            }
            finally
            {
                if (myOleDbDataReader != null)
                    myOleDbDataReader.Close();
                if (myOleDbCommand != null)
                    myOleDbCommand.Cancel();
                if (myOleDbConnection != null)
                    myOleDbConnection.Close();
                if (outputFile != null)
                    outputFile.Close();
                if (dictOutputFile != null)
                    dictOutputFile.Close();
                if (catOutputFile != null)
                    catOutputFile.Close();
            }
        }

        int fannProgress(NeuralNet net, TrainingData train, uint maxEpochs, uint epochsBetweenReports, float desiredError, uint epochs)
        {
            report("Training: epoch " + epochs + " of " + maxEpochs);
            return 0;
        }

        private void report(string s)
        {
            Invoke(new MethodInvoker(
                delegate
                {
                    textBox1.Text = s;

                }
                ));
        }


        private IDictionary<string, long> words2pos;
        private IList<string> wordsOrder;
        private IDictionary<string, long> outputValues;
        private long totalRows;

        private void prepareTrainingData(string conents, string dishType, int step, StreamWriter fileStream)
        {
            if (conents.Trim() == "" || dishType.Trim() == "")
                return;
            string text = new Regex(@"[^a-zA-Zа-яА-Я\s\.]+").Replace(conents, "");
            string[] words = split(text);
            totalRows++;
            if (step == 1)
            {
                foreach (string word in words)
                {
                    if (!words2pos.ContainsKey(word))
                    {
                        wordsOrder.Add(word);
                        words2pos.Add(word, wordsOrder.Count - 1);
                    }
                    if (!outputValues.ContainsKey(dishType))
                        outputValues.Add(dishType, outputValues.Count);
                }
            }
            else if (step == 2)
            {
                IDictionary<string, long> freqs = countFreq(words);
                //List<KeyValuePair<string, long>> sortedMap = sortMap(freqs, false);
                //List<KeyValuePair<string, long>> sortedMapRank = sortMap(freqs, true);
                foreach (string word in wordsOrder)
                {
                    if (freqs.ContainsKey(word))
                    {
                        double v;
                        if (!checkBox1.Checked) v = ((double)freqs[word]) / freqs.Count;
                        else v = Math.Min(0.1, 0.01 * freqs[word]);
                        fileStream.Write(v.ToString().Replace(',','.') + " ");
                    }
                    else
                    {
                        fileStream.Write("0 ");
                    }
                }
                fileStream.WriteLine();
                fileStream.WriteLine(outputValues[dishType] + " ");
            }

        }

        private string[] split(string p)
        {
            return p.Split(new[] { '–', '.', ',', ' ', '-', ';', '\r', '\n', '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '/', ':', '\'' }).Where(x => !String.IsNullOrEmpty(x)).Select(x => x.ToLower()).ToArray();
        }

        private void backgroundWorker1_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            Invoke(new MethodInvoker(
                 delegate
                 {
                     button1.Enabled = true;
                     tabControl1.Enabled = true;
                 }
                 ));
        }

        private IDictionary<string, long> countFreq(string[] p)
        {
            IDictionary<string, long> freqs = new Dictionary<string, long>();
            foreach (var item in p)
            {
                if (!freqs.ContainsKey(item))
                    freqs.Add(item, 0);
                long n = freqs[item];
                n++;
                freqs[item] = n;
            }
            //var freqset = new SortedSet<KeyValuePair<string, long>>((a,b)=>{return true;});
            return freqs;
        }

        private List<KeyValuePair<string, long>> sortMap(IDictionary<string, long> freqs, bool p)
        {
            List<KeyValuePair<string, long>> o = new List<KeyValuePair<string, long>>();
            foreach (var k in freqs.Keys)
            {
                var v = freqs[k];
                o.Add(new KeyValuePair<string, long>(k, v));
            }
            o.Sort(compareRank);
            return o;
        }
        private static int compareRank(KeyValuePair<string, long> x, KeyValuePair<string, long> y)
        {
            return -(x.Value.CompareTo(y.Value));
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (openFileDialog2.ShowDialog() == DialogResult.OK)
            {
                textBox2.Text = openFileDialog2.FileName.Replace(".ann","");
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            string status = "";
            if (!File.Exists(textBox2.Text + ".ann"))
                status += "ANN missing ";
            if (!File.Exists(textBox2.Text + ".words.cat"))
                status += "Words CAT missing ";
            if (!File.Exists(textBox2.Text + ".words.dict"))
                status += "Words DICT missing ";
            if (status != "")
                label7.Text = "Files: " + status;
            else
            {
                label7.Text = "";
                textBox4.Text = "";
                IDictionary<string, int> words2index = new Dictionary<string, int>();
                IDictionary<int,string> cat2index = new Dictionary<int, string>();
                StreamReader dictFileStream = null;
                StreamReader catFileStream = null;
                try
                {
                    dictFileStream = File.OpenText(textBox2.Text + ".words.dict");
                    int i = 0;
                    while (!dictFileStream.EndOfStream)
                    {
                        words2index.Add(dictFileStream.ReadLine(), i++);
                    }
                    catFileStream = File.OpenText(textBox2.Text + ".words.cat");
                    i = 0;
                    while (!catFileStream.EndOfStream)
                    {
                        cat2index.Add(i++, catFileStream.ReadLine());
                    }
                }
                finally
                {
                    if (dictFileStream != null)
                        dictFileStream.Close();
                    if (catFileStream != null)
                        catFileStream.Close();
                }
                string text = new Regex(@"[^a-zA-Zа-яА-Я\s\.]+").Replace(textBox3.Text, "");
                string[] words = split(text);

                IDictionary<string, long> freqs = countFreq(words);
                double[] args = new double[words2index.Count];
                int ctr = 0;
                foreach (var v in words2index.OrderBy(x=>x.Value).Select(x=>x.Key))
                {
                    if (freqs.ContainsKey(v))
                    {
                        if (!checkBox1.Checked)
                            args[ctr] = ((double)freqs[v]) / words2index.Count;
                        else
                        {
                            args[ctr] = Math.Min(0.1,0.01*freqs[v]);
                        }
                    }
                    ctr++;
                }
                textBox4.Text += "Args values: " + args.Aggregate(string.Empty, (s, o) => s + " " + o.ToString())+Environment.NewLine;
                NeuralNet net = new NeuralNet();

                net.CreateFromFile(textBox2.Text+".ann");
                net.PrintError();
                double[] result = net.Run(args);
                net.PrintError();
                textBox4.Text += "Results array: " + result.Aggregate(string.Empty, (s, o) => s + " " + o.ToString()) + Environment.NewLine;
                int maxpos = -1;
                double maxval = -1;
                for(int i=0;i<result.Length;i++)
                {
                    if (result[i]>maxval)
                    {
                        maxval = result[i];
                        maxpos = i;
                    }
                }
                textBox4.Text += "Max value "+maxval+" for: " + cat2index[maxpos];
            }

        }



    }
}
