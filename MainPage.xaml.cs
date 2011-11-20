using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Phone.Controls;
using Microsoft.Phone.Tasks;
using System.IO;
using System.Windows.Media.Imaging;
using Hawaii.Services.Client;
using Hawaii.Services.Client.Ocr;
using System.Text;
using System.Windows.Navigation;
using System.Text.RegularExpressions;

namespace Carded
{
    public partial class MainPage : PhoneApplicationPage
    {
        private const double ImageMaxSizeDiagonal = 600;
        
        private CameraCaptureTask cameraCaptureTask;
        private SaveContactTask saveContactTask;

        private bool showInProgress;
        private OcrServiceResult cardText;

        // Constructor
        public MainPage()
        {
            InitializeComponent();

            this.cameraCaptureTask = new CameraCaptureTask();
            this.cameraCaptureTask.Completed += new System.EventHandler<PhotoResult>(this.PhotoChooserCompleted);

            this.saveContactTask = new SaveContactTask();
            this.saveContactTask.Completed += new System.EventHandler<SaveContactResult>(this.ContactSaverCompleted);
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            if (this.showInProgress == false)
            {
                this.showInProgress = true;
                this.cameraCaptureTask.Show();
            }
        }

        private void PhotoChooserCompleted(object sender, PhotoResult e)
        {
            this.showInProgress = false;
            if (e.TaskResult == TaskResult.OK)
            {
                if (this.area1.Visibility == System.Windows.Visibility.Visible)
                {
                    this.ToggleAreas();
                }
                this.ResetArea2();
                this.ShowPhoto(e.ChosenPhoto);
                this.StartOcrConversion(e.ChosenPhoto);
            }
        }

        private void ContactSaverCompleted(object sender, SaveContactResult e)
        {
            if (e.TaskResult == TaskResult.OK)
            {
                this.ToggleAreas();
                this.ResetArea2();
            }
        }

        private void ToggleAreas()
        {
            if (this.area1.Visibility == System.Windows.Visibility.Visible)
            {
                this.area1.Visibility = System.Windows.Visibility.Collapsed;
                this.area2.Visibility = System.Windows.Visibility.Visible;
                this.PageTitle.Text = "view card";
            }
            else
            {
                this.area1.Visibility = System.Windows.Visibility.Visible;
                this.area2.Visibility = System.Windows.Visibility.Collapsed;
                this.PageTitle.Text = "instructions";
            }
        }

        private void ResetArea2()
        {
            this.textBlock5.Text = "Scanning...";
            this.button2.IsEnabled = false;
            this.button3.IsEnabled = false;
        }

        private void ShowPhoto(Stream photoStream)
        {
            BitmapImage bmp = new BitmapImage();
            bmp.SetSource(photoStream);
            this.image1.Source = bmp;
        }

        private void StartOcrConversion(Stream photoStream)
        {
            // Images that are too large will take too long to transfer to the Hawaii OCR service.
            // Also images that are too large may contain text that is too big and that will be excluded from the OCR process.
            // If necessary, we will scale-down the image.
            photoStream = MainPage.LimitImageSize(photoStream, ImageMaxSizeDiagonal);

            // Convert the photo stream to bytes.
            byte[] photoBuffer = MainPage.StreamToByteArray(photoStream);

            // Instantiate the service proxy, set the oncomplete event handler, and trigger the asynchronous call.
            OcrService.RecognizeImageAsync(
                HawaiiClient.HawaiiApplicationId,
                photoBuffer,
                (output) =>
                {
                    // This section defines the body of what is known as an anonymous method. 
                    // This anonymous method is the callback method 
                    // called on the completion of the OCR process.
                    // Using Dispatcher.BeginInvoke ensures that 
                    // OnOcrCompleted is invoked on the Main UI thread.
                    this.Dispatcher.BeginInvoke(() => OnOcrCompleted(output));
                });
        }

        private void OnOcrCompleted(OcrServiceResult result)
        {
            cardText = result;

            if (result.Status == Status.Success)
            {
                int wordCount = 0;
                StringBuilder sb = new StringBuilder();
                foreach (OcrText item in result.OcrResult.OcrTexts)
                {
                    wordCount += item.Words.Count;
                    sb.Append(item.Text);
                    sb.Append("\n");
                }

                if (wordCount > 0)
                {
                    this.textBlock5.Text = sb.ToString();
                }
                else
                {
                    this.textBlock5.Text = "ERROR: We were unable to read this card. Please check the quality of the image and try again.";
                }
            }
            else
            {
                this.textBlock5.Text = "ERROR: " + result.Exception.Message;
            }

            this.button2.IsEnabled = true;
            this.button3.IsEnabled = true;
        }

        private static Stream LimitImageSize(Stream imageStream, double imageMaxDiagonalSize)
        {
            // In order to determine the size of the image we will transfer it to a writable bitmap.
            WriteableBitmap wb = new WriteableBitmap(1, 1);
            wb.SetSource(imageStream);

            // Check if we need to scale it down.
            double imageDiagonalSize = Math.Sqrt(wb.PixelWidth * wb.PixelWidth + wb.PixelHeight * wb.PixelHeight);
            if (imageDiagonalSize > imageMaxDiagonalSize)
            {
                // Calculate the new image size that corresponds to imageMaxDiagonalSize for the 
                // diagonal size and that preserves the aspect ratio.
                int newWidth = (int)(wb.PixelWidth * imageMaxDiagonalSize / imageDiagonalSize);
                int newHeight = (int)(wb.PixelHeight * imageMaxDiagonalSize / imageDiagonalSize);

                Stream resizedStream = new MemoryStream();
                Extensions.SaveJpeg(wb, resizedStream, newWidth, newHeight, 0, 100);

                return resizedStream;
            }
            else
            {
                // No need to scale down. The image diagonal is less than or equal to imageMaxSizeDiagonal.
                return imageStream;
            }
        }

        private static byte[] StreamToByteArray(Stream stream)
        {
            byte[] buffer = new byte[stream.Length];

            long seekPosition = stream.Seek(0, SeekOrigin.Begin);
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            seekPosition = stream.Seek(0, SeekOrigin.Begin);

            return buffer;
        }

        protected override void OnBackKeyPress(System.ComponentModel.CancelEventArgs e)
        {
            if (this.area1.Visibility == System.Windows.Visibility.Collapsed)
            {
                e.Cancel = true;
                this.ToggleAreas();
            }
        }

        private void button2_Click(object sender, RoutedEventArgs e)
        {
            int i = 0;
            foreach (OcrWord w in cardText.OcrResult.OcrTexts[0].Words)
            {
                this.saveContactTask.FirstName = "";
                this.saveContactTask.LastName = "";

                // parse phone number
                Regex regex = new Regex(@"^[-+]?[0-9]*\.?[0-9]+$");
                if (regex.IsMatch(w.Text))
                {
                    if (w.Text.Length == 10)
                    {
                        this.saveContactTask.WorkPhone = w.Text;
                    }
                    else if (w.Text.Length == 3)
                    {
                        if (cardText.OcrResult.OcrTexts[0].Words[i + 1].Text.Length == 3 &&
                            cardText.OcrResult.OcrTexts[0].Words[i + 2].Text.Length == 4)
                        {
                            this.saveContactTask.WorkPhone = w.Text + cardText.OcrResult.OcrTexts[0].Words[i + 1] + cardText.OcrResult.OcrTexts[0].Words[i + 2];
                        }
                    }
                }

                // parse email address
                if (w.Text.Contains("@"))
                {
                    this.saveContactTask.WorkEmail = w.Text;
                }

                // parse website
                if (w.Text.Contains("http://") ||
                    (w.Text.Contains(".com") && !w.Text.Contains("@")))
                {
                    this.saveContactTask.Website = w.Text;
                }

                i++;
            }

            this.saveContactTask.Notes = "This contact was scanned from a business card using Carded, a Windows Phone application. The scanned text is included below.\n\n" + cardText.OcrResult.OcrTexts[0].Text;

            this.saveContactTask.Show();
        }

        private void button3_Click(object sender, RoutedEventArgs e)
        {
            this.button1_Click(sender, e);
        }
    }
}