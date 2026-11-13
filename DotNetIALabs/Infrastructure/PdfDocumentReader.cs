using Microsoft.Extensions.DataIngestion;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DotNetIALabs.Infrastructure
{
    public sealed class PdfDocumentReader : IngestionDocumentReader
    {
        public override Task<IngestionDocument> ReadAsync(
            Stream source,
            string identifier,
            string mediaType,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            //Copie locale : le flx fourni reste sous la responsabilité de l'appelant
            // PdfPig reçoit un flux seekable.
            using var buffer = new MemoryStream();
            source.CopyTo(buffer);
            buffer.Position = 0;

            using var pdf = PdfDocument.Open(buffer);

            var document = new IngestionDocument(identifier);

            foreach(var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var text = ContentOrderTextExtractor.GetText(page);

                if (string.IsNullOrWhiteSpace(text))
                    continue;

                var section = new IngestionDocumentSection
                {
                    PageNumber = page.Number
                };

                section.Elements.Add(
                    new IngestionDocumentParagraph(text));

                document.Sections.Add(section);
            }

            return Task.FromResult(document);
        }

    }
}
