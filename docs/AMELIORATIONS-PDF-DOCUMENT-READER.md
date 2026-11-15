# Amélioration de `PdfDocumentReader` avec PdfPig

## 1. Objectif

Ce document décrit les améliorations pouvant être apportées à
`PdfDocumentReader` tout en conservant **PdfPig** comme moteur principal de
lecture des fichiers PDF.

L'objectif immédiat reste pédagogique : comprendre progressivement les
difficultés propres à l'ingestion PDF avant d'adopter un moteur industriel de
Document Intelligence.

Les améliorations proposées doivent permettre de :

- mieux reconstruire les paragraphes et l'ordre de lecture ;
- conserver le numéro de page et les métadonnées de provenance ;
- extraire les images incorporées dans les PDF ;
- ajouter un OCR de secours pour les pages scannées ;
- reconnaître des tableaux simples ;
- produire des éléments `Microsoft.Extensions.DataIngestion` structurés ;
- préparer des chunks plus pertinents pour la recherche vectorielle et le RAG ;
- conserver une architecture simple et compatible avec le monoprojet actuel.

Ce document constitue une cible progressive. Toutes les fonctionnalités ne
doivent pas être implémentées en une seule étape.

---

## 2. Périmètre

### Inclus

- PDF contenant une couche texte ;
- documents à une ou plusieurs colonnes ;
- images incorporées dans les pages ;
- PDF scannés traités par un OCR local de secours ;
- tableaux simples avec ou sans bordures ;
- métadonnées utiles au RAG ;
- diagnostic de la qualité d'extraction.

### Hors périmètre initial

- reconnaissance parfaite des tableaux avec cellules fusionnées ;
- fusion automatique de tableaux sur plusieurs pages ;
- reconnaissance fiable de documents manuscrits ;
- compréhension avancée de graphiques et schémas ;
- extraction de formulaires métier complexes ;
- remplacement de PdfPig par Docling, Azure Document Intelligence ou un autre
  moteur industriel.

---

## 3. État actuel

Le reader actuel :

1. ouvre le flux avec `PdfDocument.Open` ;
2. parcourt les pages ;
3. utilise `ContentOrderTextExtractor.GetText(page)` ;
4. produit un seul `IngestionDocumentParagraph` par page ;
5. ignore les pages sans texte ;
6. échoue si aucune page ne contient de texte.

Cette implémentation convient aux PDF textuels simples, mais présente plusieurs
limites :

- une page entière devient un seul paragraphe ;
- l'ordre de lecture des documents multicolonnes peut être incorrect ;
- les titres, listes et paragraphes ne sont pas distingués ;
- les images ne sont pas produites comme éléments d'ingestion ;
- les PDF scannés ne sont pas lus ;
- les tableaux sont aplatis en texte ;
- aucune mesure de qualité d'extraction n'est conservée.

---

## 4. Architecture cible simple

`PdfDocumentReader` reste le point d'entrée utilisé par
`IngestionPipeline<string>`. Il orchestre des composants spécialisés sans
contenir lui-même tous les algorithmes.

```text
PdfDocumentReader
    |
    +--> PdfPig
    |      +--> pages
    |      +--> mots et coordonnées
    |      +--> blocs de texte
    |      +--> images incorporées
    |      +--> traits graphiques
    |
    +--> IPdfPageOcrService
    |      +--> traitement des pages sans texte exploitable
    |
    +--> IPdfTableExtractor
           +--> détection pédagogique des tableaux simples
```

Les interfaces OCR et table ne doivent être ajoutées qu'au moment où leur
implémentation commence. La première amélioration peut rester entièrement dans
`PdfDocumentReader` et utiliser uniquement les outils d'analyse de PdfPig.

### Organisation proposée

```text
Infrastructure/
    Pdf/
        PdfDocumentReader.cs
        PdfReaderOptions.cs
        PdfTextLayoutExtractor.cs
        PdfImageExtractor.cs
        Ocr/
            IPdfPageOcrService.cs
            TesseractPdfPageOcrService.cs
        Tables/
            IPdfTableExtractor.cs
            SimplePdfTableExtractor.cs
```

Pour préserver la progression du lab, cette arborescence peut être introduite
graduellement. Il n'est pas nécessaire de déplacer immédiatement le reader
actuel.

---

## 5. Configuration du reader

Les comportements optionnels doivent être configurables.

```csharp
namespace DotNetIALabs.Infrastructure.Pdf;

public sealed class PdfReaderOptions
{
    public const string SectionName = "PdfIngestion";

    public bool ExtractImages { get; init; } = true;

    public bool EnableOcrFallback { get; init; }

    public bool DetectSimpleTables { get; init; }

    public bool RemoveRepeatedHeadersAndFooters { get; init; } = true;

    public int MinimumExtractedCharactersPerPage { get; init; } = 20;

    public int MinimumImageWidth { get; init; } = 100;

    public int MinimumImageHeight { get; init; } = 100;

    public int MaximumPageCount { get; init; } = 500;

    public long MaximumDocumentSizeBytes { get; init; } = 50 * 1024 * 1024;

    public float MinimumOcrConfidence { get; init; } = 0.50f;
}
```

Exemple de configuration :

```json
{
  "PdfIngestion": {
    "ExtractImages": true,
    "EnableOcrFallback": false,
    "DetectSimpleTables": false,
    "RemoveRepeatedHeadersAndFooters": true,
    "MinimumExtractedCharactersPerPage": 20,
    "MinimumImageWidth": 100,
    "MinimumImageHeight": 100,
    "MaximumPageCount": 500,
    "MaximumDocumentSizeBytes": 52428800,
    "MinimumOcrConfidence": 0.5
  }
}
```

L'OCR et la détection des tableaux sont initialement désactivés afin de garder
un comportement prévisible pendant les premiers exercices.

---

## 6. Amélioration 1 — Reconstruction des blocs de texte

### Problème

`ContentOrderTextExtractor` fournit un texte lisible dans de nombreux cas,
mais le reader actuel transforme toute la page en un seul paragraphe. Cette
structure est trop grossière pour un chunking sémantique de qualité.

### Fonctionnalités PdfPig utilisables

PdfPig propose :

- `NearestNeighbourWordExtractor` pour reconstruire les mots ;
- `RecursiveXYCut` pour séparer les colonnes et les blocs ;
- `DocstrumBoundingBoxes` pour regrouper les mots en lignes et paragraphes ;
- `UnsupervisedReadingOrderDetector` pour déterminer l'ordre de lecture.

### Implémentation proposée

```csharp
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

private static IEnumerable<string> ExtractTextBlocks(Page page)
{
    IReadOnlyList<Word> words = page
        .GetWords(NearestNeighbourWordExtractor.Instance)
        .Where(word => !string.IsNullOrWhiteSpace(word.Text))
        .ToList();

    if (words.Count == 0)
    {
        yield break;
    }

    IReadOnlyList<TextBlock> blocks =
        DocstrumBoundingBoxes.Instance
            .GetBlocks(words)
            .ToList();

    IEnumerable<TextBlock> orderedBlocks =
        UnsupervisedReadingOrderDetector.Instance.Get(blocks);

    foreach (TextBlock block in orderedBlocks)
    {
        string text = block.Text.Trim();

        if (!string.IsNullOrWhiteSpace(text))
        {
            yield return text;
        }
    }
}
```

Chaque bloc devient un paragraphe distinct :

```csharp
foreach (string textBlock in ExtractTextBlocks(page))
{
    IngestionDocumentParagraph paragraph = new(textBlock)
    {
        PageNumber = page.Number
    };

    paragraph.Metadata["source"] = "pdf-text";
    paragraph.Metadata["page_number"] = page.Number;

    section.Elements.Add(paragraph);
}
```

### Stratégie de repli

Les algorithmes de mise en page peuvent produire un résultat insuffisant sur
certains PDF. Le reader peut revenir à `ContentOrderTextExtractor` si aucun
bloc n'est obtenu :

```csharp
List<string> blocks = ExtractTextBlocks(page).ToList();

if (blocks.Count == 0)
{
    string fallbackText =
        ContentOrderTextExtractor.GetText(
            page,
            addDoubleNewline: true);

    if (!string.IsNullOrWhiteSpace(fallbackText))
    {
        blocks.Add(fallbackText);
    }
}
```

---

## 7. Amélioration 2 — Nettoyage du texte

### Lettres dupliquées

Certains générateurs PDF simulent le texte en gras en superposant deux lettres.
Cela peut produire des mots comme `DDoonnnnééeess`.

PdfPig fournit `DuplicateOverlappingTextProcessor` pour réduire ce problème.
Son utilisation doit être évaluée sur les PDF du corpus avant d'être activée
systématiquement.

### Normalisation

Le nettoyage peut inclure :

- normalisation Unicode `NormalizationForm.FormKC` ;
- réduction des espaces répétés ;
- conservation des sauts de paragraphe ;
- suppression des caractères de contrôle ;
- réparation limitée des mots coupés en fin de ligne ;
- exclusion des blocs ne contenant que des numéros de page.

Il ne faut pas supprimer agressivement la ponctuation ou les retours à la ligne,
car ils aident le chunker à préserver la structure sémantique.

---

## 8. Amélioration 3 — En-têtes et pieds de page

Les en-têtes répétés polluent les embeddings et peuvent apparaître dans de
nombreux résultats de recherche.

Une stratégie pédagogique consiste à :

1. extraire les blocs de toutes les pages ;
2. normaliser leur texte ;
3. repérer les blocs identiques présents sur une majorité de pages ;
4. vérifier qu'ils se trouvent près du bord supérieur ou inférieur ;
5. les marquer comme en-tête ou pied de page ;
6. les exclure du contenu indexé ou les produire comme éléments dédiés.

Critère possible :

```text
bloc répété sur au moins 60 % des pages
et situé dans les 10 % supérieurs ou inférieurs de la page
```

La suppression doit rester configurable, car une information répétée peut être
fonctionnellement importante dans certains documents.

---

## 9. Amélioration 4 — Extraction des images

PdfPig expose les images incorporées avec `page.GetImages()`.

```csharp
using UglyToad.PdfPig.Content;

private static void AddImages(
    IngestionDocumentSection section,
    Page page,
    PdfReaderOptions options)
{
    int imageNumber = 0;

    foreach (IPdfImage pdfImage in page.GetImages())
    {
        imageNumber++;

        if (pdfImage.WidthInSamples < options.MinimumImageWidth ||
            pdfImage.HeightInSamples < options.MinimumImageHeight)
        {
            continue;
        }

        if (!pdfImage.TryGetPng(out byte[]? content))
        {
            continue;
        }

        IngestionDocumentImage image = new(
            $"![Image {imageNumber} de la page {page.Number}]" +
            $"(embedded://page-{page.Number}/image-{imageNumber})")
        {
            Content = content,
            MediaType = "image/png",
            PageNumber = page.Number
        };

        image.Metadata["source"] = "pdf-image";
        image.Metadata["page_number"] = page.Number;
        image.Metadata["width"] = pdfImage.WidthInSamples;
        image.Metadata["height"] = pdfImage.HeightInSamples;

        section.Elements.Add(image);
    }
}
```

### Limites

- toutes les images PDF ne peuvent pas être converties par `TryGetPng` ;
- un graphique vectoriel n'est pas nécessairement une image incorporée ;
- les logos et décorations doivent être filtrés ;
- l'ordre exact entre texte et images doit être reconstruit avec leurs
  coordonnées si ce besoin devient important.

`PdfPig.Rendering.Skia` peut être évalué ultérieurement pour améliorer le rendu
des images. Sa version doit être vérifiée avec la version de PdfPig utilisée par
le projet avant d'ajouter le package.

### Enrichissement des images

`ImageAlternativeTextEnricher` ne doit être activé que lorsque :

1. le reader produit réellement des `IngestionDocumentImage` ;
2. `Content` et `MediaType` sont renseignés ;
3. le `IChatClient` utilisé accepte les entrées image ;
4. le modèle configuré est un modèle vision.

Le modèle textuel actuellement utilisé pour les résumés ne doit pas être
supposé compatible avec les images.

---

## 10. Amélioration 5 — OCR de secours

### Principe

PdfPig ne contient pas de moteur OCR. Il reste néanmoins le moteur de lecture et
de navigation dans le PDF. L'OCR est déclenché uniquement lorsqu'une page ne
contient pas suffisamment de texte exploitable.

```text
Page PDF
    |
    +--> PdfPig extrait suffisamment de texte
    |       -> conserver le texte natif
    |
    +--> texte absent ou insuffisant
            -> rendre la page en image
            -> exécuter l'OCR
            -> produire un paragraphe OCR
```

### Détection d'une page nécessitant l'OCR

```csharp
private static bool RequiresOcr(
    IReadOnlyCollection<string> blocks,
    PdfReaderOptions options)
{
    int characterCount = blocks.Sum(block =>
        block.Count(character => !char.IsWhiteSpace(character)));

    return characterCount <
        options.MinimumExtractedCharactersPerPage;
}
```

Une page comportant quelques caractères décoratifs ne doit pas être considérée
comme correctement extraite.

### Contrat OCR

```csharp
namespace DotNetIALabs.Infrastructure.Pdf.Ocr;

public interface IPdfPageOcrService
{
    Task<PdfOcrResult> RecognizeAsync(
        ReadOnlyMemory<byte> pageImage,
        CancellationToken cancellationToken = default);
}
```

```csharp
namespace DotNetIALabs.Infrastructure.Pdf.Ocr;

public sealed record PdfOcrResult
{
    public required string Text { get; init; }

    public float? Confidence { get; init; }

    public string Language { get; init; } = "unknown";
}
```

### Rendu de la page

L'OCR nécessite une image complète de la page. PdfPig ne réalise pas directement
ce rendu. Une extension comme `PdfPig.Rendering.Skia` peut être utilisée tout en
conservant PdfPig comme moteur PDF.

Le rendu doit idéalement produire une image équivalente à environ 300 DPI pour
obtenir une précision OCR acceptable sans consommation excessive.

### Moteur OCR pédagogique

Tesseract peut être utilisé localement via un wrapper .NET. Les données de langue
doivent être installées, par exemple :

```text
fra         français
eng         anglais
fra+eng     document bilingue
```

Tesseract reconnaît le texte, mais ne reconstruit pas à lui seul la structure
complète d'un tableau.

### Création de l'élément OCR

```csharp
private static void AddOcrParagraph(
    IngestionDocumentSection section,
    PdfOcrResult result,
    int pageNumber,
    PdfReaderOptions options)
{
    if (string.IsNullOrWhiteSpace(result.Text))
    {
        return;
    }

    if (result.Confidence is float confidence &&
        confidence < options.MinimumOcrConfidence)
    {
        return;
    }

    IngestionDocumentParagraph paragraph = new(result.Text.Trim())
    {
        PageNumber = pageNumber
    };

    paragraph.Metadata["source"] = "ocr";
    paragraph.Metadata["page_number"] = pageNumber;
    paragraph.Metadata["ocr_language"] = result.Language;

    if (result.Confidence is not null)
    {
        paragraph.Metadata["ocr_confidence"] =
            result.Confidence.Value;
    }

    section.Elements.Add(paragraph);
}
```

### Nouvelle règle d'échec

L'exception actuelle signalant qu'aucun texte n'a été extrait doit intervenir
après la tentative OCR :

```text
extraction native vide
    -> OCR activé ?
        -> oui : tentative OCR
        -> non : échec explicite
    -> résultat toujours vide : échec explicite
```

---

## 11. Amélioration 6 — Détection pédagogique des tableaux

### Limitation de PdfPig

PdfPig expose les mots, leurs coordonnées et les traits graphiques, mais ne
retourne pas directement une structure de tableau.

La première implémentation doit volontairement se limiter aux tableaux simples.

### Contrat proposé

```csharp
namespace DotNetIALabs.Infrastructure.Pdf.Tables;

public interface IPdfTableExtractor
{
    IReadOnlyList<PdfExtractedTable> Extract(
        UglyToad.PdfPig.Content.Page page,
        CancellationToken cancellationToken = default);
}
```

```csharp
namespace DotNetIALabs.Infrastructure.Pdf.Tables;

public sealed record PdfExtractedTable
{
    public required string[,] Cells { get; init; }

    public required int PageNumber { get; init; }

    public required IReadOnlyCollection<string> SourceWordIds { get; init; }
}
```

`SourceWordIds` représente conceptuellement les mots consommés par le tableau.
Une implémentation réelle peut utiliser des coordonnées ou un identifiant
interne. Le but est d'éviter que le même contenu soit produit une fois comme
tableau et une seconde fois comme paragraphe.

### Tableaux avec bordures

Pour un tableau dessiné avec une grille :

1. lire `page.Paths` ;
2. identifier les segments horizontaux et verticaux ;
3. fusionner les segments presque alignés ;
4. calculer les intersections ;
5. construire les rectangles des cellules ;
6. affecter chaque mot à la cellule qui contient son centre ;
7. trier les mots de chaque cellule selon l'ordre de lecture ;
8. générer la matrice de cellules.

Une tolérance doit être appliquée, car les coordonnées PDF sont rarement
parfaitement identiques.

### Tableaux sans bordures

Pour un tableau sans traits visibles :

1. regrouper les mots dont les coordonnées verticales sont proches ;
2. identifier les alignements horizontaux répétés ;
3. déterminer des limites de colonnes candidates ;
4. exiger plusieurs lignes partageant la même structure ;
5. rejeter les zones contenant principalement du texte long.

Cette détection sera moins fiable. Elle doit être activée séparément ou limitée
à des documents connus.

### Conversion en `IngestionDocumentTable`

```csharp
private static IngestionDocumentTable CreateTableElement(
    PdfExtractedTable extractedTable)
{
    int rowCount = extractedTable.Cells.GetLength(0);
    int columnCount = extractedTable.Cells.GetLength(1);

    IngestionDocumentElement[,] cells =
        new IngestionDocumentElement[rowCount, columnCount];

    for (int row = 0; row < rowCount; row++)
    {
        for (int column = 0; column < columnCount; column++)
        {
            string value = extractedTable.Cells[row, column];

            cells[row, column] =
                new IngestionDocumentParagraph(
                    string.IsNullOrWhiteSpace(value) ? " " : value);
        }
    }

    string markdown = BuildMarkdownTable(extractedTable.Cells);

    IngestionDocumentTable table = new(markdown, cells)
    {
        PageNumber = extractedTable.PageNumber
    };

    table.Metadata["source"] = "pdf-table-layout";
    table.Metadata["page_number"] = extractedTable.PageNumber;

    return table;
}
```

`BuildMarkdownTable` doit notamment échapper les caractères `|` et les retours
à la ligne présents dans les cellules.

### Limites acceptées

La première version ne doit pas promettre la prise en charge de :

- cellules fusionnées ;
- tableaux imbriqués ;
- tableaux tournés ;
- tableaux répartis sur plusieurs pages ;
- cellules contenant des images ;
- tableaux sans alignement stable.

Ces cas doivent produire un avertissement ou rester sous forme de texte.

---

## 12. Ordre des éléments dans une page

Ajouter les paragraphes, puis les tableaux, puis les images est simple, mais ne
respecte pas nécessairement l'ordre visuel du document.

Une évolution ultérieure consiste à représenter chaque élément extrait avec sa
boîte englobante :

```csharp
public sealed record PdfPageElement
{
    public required IngestionDocumentElement Element { get; init; }

    public required double Top { get; init; }

    public required double Left { get; init; }
}
```

Les éléments peuvent alors être triés par colonne et position verticale avant
d'être ajoutés à la section.

Cette étape est importante pour associer correctement :

- une image et sa légende ;
- un titre et son paragraphe ;
- un tableau et son titre ;
- plusieurs colonnes d'une même page.

---

## 13. Métadonnées à conserver

Chaque élément ou chunk doit pouvoir être relié à sa source.

Métadonnées minimales recommandées :

```text
document_id
source_path
media_type
page_number
element_type
extraction_source
ocr_confidence
reader_version
```

Valeurs possibles pour `extraction_source` :

```text
pdf-text
pdf-image
pdf-table-layout
ocr
```

Ces informations permettront :

- d'afficher des citations par page ;
- de diagnostiquer une réponse incorrecte ;
- de comparer le texte natif et le texte OCR ;
- de réingérer les documents après une évolution du reader ;
- de filtrer les résultats de recherche.

Le numéro de version du reader doit évoluer lorsqu'un changement modifie le
contenu indexé :

```csharp
private const string ReaderVersion = "pdfpig-reader-v2";
```

---

## 14. Orchestration cible du reader

Le code suivant illustre le flux cible. Il ne représente pas une implémentation
complète des services OCR, rendu et tableaux.

```csharp
public sealed class PdfDocumentReader(
    IOptions<PdfReaderOptions> options,
    IPdfPageOcrService? ocrService = null,
    IPdfTableExtractor? tableExtractor = null)
    : IngestionDocumentReader
{
    public override async Task<IngestionDocument> ReadAsync(
        Stream source,
        string identifier,
        string mediaType,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using PdfDocument pdf = PdfDocument.Open(source);

        ValidateDocument(pdf, options.Value);

        IngestionDocument document = new(identifier);

        document.Metadata["media_type"] = mediaType;
        document.Metadata["page_count"] = pdf.NumberOfPages;
        document.Metadata["reader_version"] = ReaderVersion;

        for (int pageNumber = 1;
             pageNumber <= pdf.NumberOfPages;
             pageNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Page page = pdf.GetPage(pageNumber);

            IngestionDocumentSection section = new()
            {
                PageNumber = pageNumber
            };

            List<string> textBlocks =
                ExtractTextBlocks(page).ToList();

            if (!RequiresOcr(textBlocks, options.Value))
            {
                AddTextBlocks(section, textBlocks, pageNumber);
            }
            else if (options.Value.EnableOcrFallback &&
                     ocrService is not null)
            {
                PdfOcrResult ocrResult =
                    await ExtractPageWithOcrAsync(
                        pdf,
                        page,
                        ocrService,
                        cancellationToken);

                AddOcrParagraph(
                    section,
                    ocrResult,
                    pageNumber,
                    options.Value);
            }

            if (options.Value.DetectSimpleTables &&
                tableExtractor is not null)
            {
                AddTables(
                    section,
                    tableExtractor.Extract(
                        page,
                        cancellationToken));
            }

            if (options.Value.ExtractImages)
            {
                AddImages(section, page, options.Value);
            }

            if (section.Elements.Count > 0)
            {
                document.Sections.Add(section);
            }
        }

        if (document.Sections.Count == 0)
        {
            throw new InvalidDataException(
                $"No usable content could be extracted from " +
                $"PDF '{identifier}'.");
        }

        return document;
    }
}
```

### Remarque sur l'asynchronisme

PdfPig expose principalement des opérations synchrones. Il ne faut pas utiliser
`Task.Run` automatiquement pour masquer cette caractéristique. Les opérations
réellement asynchrones, comme un OCR externe ou la lecture d'un service, doivent
être attendues normalement et propager le `CancellationToken`.

---

## 15. Validation et sécurité

Avant de traiter un PDF :

- vérifier que le flux est lisible ;
- appliquer une taille maximale ;
- appliquer un nombre maximal de pages ;
- gérer les PDF chiffrés ou protégés par mot de passe ;
- intercepter les PDF invalides avec un message explicite ;
- limiter le nombre d'images extraites ;
- limiter la taille totale des images ;
- appliquer un délai maximal à l'OCR ;
- ne pas journaliser le contenu complet des documents ;
- ne pas conserver de fichiers temporaires sans politique de suppression.

Les documents provenant d'une GED doivent être considérés comme des entrées non
fiables, même s'ils sont internes à l'organisation.

---

## 16. Journalisation et diagnostic

Les événements utiles sont :

```text
PDF ouvert : nombre de pages
Page traitée : texte natif, OCR ou mixte
Nombre de blocs de texte
Nombre d'images extraites ou ignorées
Nombre de tableaux détectés
Confiance OCR moyenne
Durée par page
Avertissements de conversion
```

Exemple :

```csharp
logger.LogInformation(
    "Extracted page {PageNumber} from {DocumentId}. " +
    "Text blocks: {TextBlockCount}, images: {ImageCount}, " +
    "tables: {TableCount}, source: {ExtractionSource}.",
    pageNumber,
    identifier,
    textBlockCount,
    imageCount,
    tableCount,
    extractionSource);
```

Le texte complet et les données binaires ne doivent pas apparaître dans les
logs.

---

## 17. Injection de dépendances

La première étape sans OCR peut conserver :

```csharp
services.AddSingleton<PdfDocumentReader>();
```

Après ajout des composants optionnels :

```csharp
services
    .AddOptions<PdfReaderOptions>()
    .Bind(configuration.GetSection(PdfReaderOptions.SectionName))
    .ValidateOnStart();

services.AddSingleton<IPdfPageOcrService,
    TesseractPdfPageOcrService>();

services.AddSingleton<IPdfTableExtractor,
    SimplePdfTableExtractor>();

services.AddSingleton<PdfDocumentReader>();
```

Ces services peuvent être singleton uniquement s'ils sont thread-safe. Un
moteur OCR natif non thread-safe doit gérer une synchronisation interne ou être
enregistré avec une durée de vie adaptée.

---

## 18. Intégration avec le pipeline d'ingestion

Le pipeline général ne doit pas connaître les détails de PdfPig :

```csharp
using IngestionPipeline<string> pipeline = new(
    pdfDocumentReader,
    chunker,
    writer,
    loggerFactory: loggerFactory);
```

Si des images sont produites et qu'un modèle vision est disponible :

```csharp
pipeline.DocumentProcessors.Add(
    new ImageAlternativeTextEnricher(
        visionEnricherOptions));
```

Le résumé des chunks et la génération des embeddings restent indépendants du
format PDF.

---

## 19. Stratégie de chunking

Le reader améliore la structure du document, mais le chunker doit l'exploiter.

Recommandations :

- ne pas mélanger plusieurs pages sans conserver leurs numéros ;
- garder les titres avec les paragraphes qu'ils introduisent ;
- produire des chunks de tableau distincts ;
- répéter les en-têtes de colonnes lors du découpage d'un grand tableau ;
- intégrer la description d'une image dans le chunk proche de sa légende ;
- éviter que les en-têtes et pieds de page deviennent des chunks autonomes ;
- conserver `document_id`, `page_number` et `element_type` dans les métadonnées
  écrites dans pgvector.

---

## 20. Tests recommandés

### Corpus minimal

Créer un petit corpus de PDF couvrant :

1. PDF textuel à une colonne ;
2. PDF textuel à deux colonnes ;
3. PDF contenant des titres et listes ;
4. PDF contenant une image incorporée ;
5. PDF entièrement scanné ;
6. PDF mixte avec texte natif et pages scannées ;
7. tableau simple avec bordures ;
8. tableau simple sans bordures ;
9. PDF avec en-tête et pied de page répétés ;
10. PDF invalide ou protégé.

### Tests unitaires

- une page textuelle produit plusieurs paragraphes cohérents ;
- chaque élément possède le bon numéro de page ;
- les petites images sont ignorées ;
- une page sans texte déclenche l'OCR si celui-ci est activé ;
- une page OCR sous le seuil de confiance est rejetée ;
- un tableau simple produit une matrice correcte ;
- les mots du tableau ne sont pas dupliqués dans les paragraphes ;
- l'annulation interrompt le traitement entre les pages ;
- un document sans contenu exploitable échoue explicitement.

### Tests d'intégration

- ingestion complète vers PostgreSQL/pgvector ;
- présence du numéro de page dans les enregistrements ;
- recherche d'une information provenant d'une page OCR ;
- recherche d'une valeur présente dans un tableau ;
- réingestion après modification de la version du reader ;
- vérification de l'absence de chunks composés uniquement d'en-têtes répétés.

---

## 21. Mesures de qualité

Avant de conclure qu'une amélioration est utile, comparer :

- nombre de pages correctement extraites ;
- nombre de pages nécessitant l'OCR ;
- confiance OCR moyenne ;
- respect de l'ordre de lecture ;
- exactitude des cellules de tableaux ;
- nombre de chunks générés ;
- précision des résultats de recherche ;
- capacité du chatbot à citer la bonne page ;
- temps et mémoire consommés par document.

Un résultat visuellement propre n'est pas suffisant. La mesure principale reste
la capacité du système RAG à retrouver une information correcte avec sa source.

---

## 22. Plan d'implémentation progressif

### Étape 1 — Structure du texte

- remplacer le paragraphe unique par des blocs PdfPig ;
- tester `DocstrumBoundingBoxes` ;
- appliquer `UnsupervisedReadingOrderDetector` ;
- conserver le fallback `ContentOrderTextExtractor` ;
- ajouter les métadonnées de page et de source.

### Étape 2 — Nettoyage

- normaliser le texte ;
- traiter les lettres superposées ;
- identifier les en-têtes et pieds de page répétés ;
- ajouter les journaux de diagnostic.

### Étape 3 — Images

- produire des `IngestionDocumentImage` ;
- filtrer les petites images ;
- mesurer les formats non convertibles ;
- tester un modèle vision avant de réactiver l'enrichisseur d'images.

### Étape 4 — OCR

- ajouter un renderer de page ;
- intégrer Tesseract ;
- déclencher l'OCR seulement sur les pages sans texte ;
- conserver la langue et la confiance OCR ;
- déplacer l'erreur « aucun texte » après la tentative OCR.

### Étape 5 — Tableaux avec bordures

- analyser `page.Paths` ;
- détecter les lignes et cellules ;
- affecter les mots aux cellules ;
- produire `IngestionDocumentTable` ;
- empêcher la duplication dans les paragraphes.

### Étape 6 — Tableaux sans bordures

- regrouper les mots par lignes ;
- détecter les colonnes stables ;
- appliquer un seuil de confiance ;
- revenir au texte normal en cas d'incertitude.

### Étape 7 — Évaluation RAG

- comparer les résultats avant et après ;
- vérifier les citations par page ;
- ajuster le chunking des tableaux ;
- décider, sur la base des résultats, si un moteur documentaire plus robuste est
  nécessaire.

---

## 23. Dépendances possibles

### Dépendance actuelle

```xml
<PackageReference Include="PdfPig" Version="0.1.16" />
```

### Dépendances optionnelles à évaluer

```text
PdfPig.Rendering.Skia   rendu des pages et traitement d'images
Tesseract               wrapper .NET du moteur OCR Tesseract
```

Les versions doivent être explicitement fixées après vérification de leur
compatibilité avec .NET 10 et PdfPig `0.1.16`.

Un package ne doit être ajouté qu'au début de l'étape qui l'utilise.

---

## 24. Critères indiquant les limites de cette approche

Le passage à un moteur de Document Intelligence devra être envisagé lorsque le
corpus réel contient majoritairement :

- des scans de mauvaise qualité ;
- des documents manuscrits ;
- des tableaux complexes ou multipages ;
- des cellules fusionnées ;
- des formulaires très variables ;
- des graphiques dont le contenu doit être compris ;
- des mises en page nécessitant une précision élevée et mesurable.

Cette limite ne remet pas en cause l'intérêt pédagogique du reader PdfPig. Les
contrats OCR, table et extraction structurée pourront être réutilisés ou adaptés
lors d'une migration ultérieure.

---

## 25. Résultat cible

À la fin de cette progression, `PdfDocumentReader` doit pouvoir produire :

```text
IngestionDocument
    |
    +--> Page 1
    |      +--> paragraphes ordonnés
    |      +--> tableau simple
    |      +--> image
    |
    +--> Page 2
           +--> texte issu de l'OCR
           +--> confiance OCR
```

Chaque élément doit conserver sa page, sa méthode d'extraction et les
métadonnées nécessaires à la citation.

PdfPig reste responsable de l'ouverture du PDF, de la navigation dans les pages
et de l'accès au contenu natif. Les services complémentaires interviennent
uniquement lorsque PdfPig ne couvre pas directement le besoin, notamment pour
l'OCR et la reconstruction pédagogique des tableaux.

---

## 26. Références

- PdfPig : <https://github.com/UglyToad/PdfPig>
- Analyse de mise en page PdfPig :
  <https://github.com/UglyToad/PdfPig/wiki/Document-Layout-Analysis>
- Images avec PdfPig : <https://github.com/UglyToad/PdfPig/wiki/Images>
- FAQ et `PdfPig.Rendering.Skia` :
  <https://github.com/UglyToad/PdfPig/wiki/FAQ>
- Wrapper .NET Tesseract : <https://github.com/charlesw/tesseract>

