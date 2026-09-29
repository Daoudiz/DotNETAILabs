using System;
using System.Collections.Generic;
using System.Text;

namespace DotNetIALabs.Data
{
    public sealed record CloudServiceSeed(
    int Key,
    string Name,
    string Description);


    public static class CloudServicesCatalog
    {
        public static IReadOnlyList<CloudServiceSeed> All { get; } =
        Array.AsReadOnly<CloudServiceSeed>(
        [
            new(
                1,
                "Azure App Service",
                "Héberge des applications web et des API sans gérer les serveurs."),
            new(
                2,
                "Azure Service Bus",
                "Service de messagerie pour découpler les applications avec des files et des publications."),
            new(
                3,
                "Azure Blob Storage",
                "Stocke des fichiers et des documents dans le cloud de façon durable et extensible."),
            new(
                4,
                "Azure Key Vault",
                "Conserve de manière sécurisée les secrets, certificats, clés API et chaînes de connexion.")
        ]);
    }
}
