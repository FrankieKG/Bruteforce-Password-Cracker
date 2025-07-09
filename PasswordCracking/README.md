# PasswordCracking

Ett projekt för att demonstrera olika metoder för lösenordskrackning, inklusive brute force och ordboksattacker.

## Innehållsförteckning

- [Installation](#installation)
- [Användning](#användning)
- [Funktioner](#funktioner)
- [Licens](#licens)


## Installation

Följ dessa steg för att installera och konfigurera projektet.

1. Klona repot:
    ```bash
    git clone https://github.com/användarnamn/PasswordCracking.git
    ```

2. Gå in i projektkatalogen:
    ```bash
    cd PasswordCracking
    ```

3. Bygg projektet (förutsatt att du har .NET SDK installerat):
    ```bash
    dotnet build
    ```

## Användning

Så här använder du projektet:

```bash
dotnet run --project PasswordCracking
```

### Exempel

1. Du kan specificera antalet lösenord, längden på lösenorden och vilka tecken som ska användas för brute force-attacker i Cracker.cs.

       int numberOfPasswords = 3;
       int passWordLength = 4;
    
       BruteForceCracker bruteForceCracker = new BruteForceCracker(characterSet: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()", maxLength);

3. För att generera komplexa lösenord används PasswordGenerator.cs där teckenuppsättningen definieras:

       const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()";

### Lösenordshashning

Lösenorden hashas med hjälp av SHA-256 som definieras i PasswordHasher.cs:

    public static string HashPassword(string password)
    {
      using (SHA256 sha256 = SHA256.Create())
      {
        byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
        return BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();
      }
    }

### ordboksattacker

En lista med vanliga lösenord som används för ordboksattacker finns i Cracker.cs:

    string[] commonPasswords = { "password", "123456", "qwerty", "abc123", "admin" };

# Funktioner

Projektet inkluderar följande funktioner:

* Brute force-attacker
* Ordboksattacker
* Lösenordsgenerator
* Lösenordshasher

## Filbeskrivning

* .gitattributes: Git-attribut för att hantera linjeavslutningar och andra inställningar.
* .gitignore: Lista över filer och kataloger som ska ignoreras av Git.
* BruteForceCracker.cs: Implementering av brute force-metoden för lösenordskrackning.
* Cracker.cs: Bas-klass för olika krackningsmetoder, inklusive inställningar för antal lösenord, längd och teckenuppsättningar.
* DictionaryAttackCracker.cs: Implementering av ordboksattackmetoden.
* Kernel.cs: Kärnlogik för projektet.
* PasswordCracking.csproj: Projektfil för .NET.
* PasswordCracking.sln: Lösningsfil för Visual Studio.
* PasswordGenerator.cs: Genererar lösenord med specifika teckenuppsättningar.
* PasswordHasher.cs: Hasher lösenord med SHA-256.
* sha256.cl: SHA-256 implementering i OpenCL.

## Systemöversikt
Applikationen är en GPU-baserad lösenordsknäckare som använder brute force-taktik. Den använder OpenCL för att utnyttja GPU:ns kapacitet för parallellarbete. Applikationen är utvecklad i Visual Studio 2022 och implementerar SHA-256-algoritmen för att generera och knäcka lösenordshashar. Strukturen består av flera huvudkomponenter, inklusive en lösenordsgenerator, en GPU-baserad hashfunktion, en CPU-baserad hashfunktion och en bruteforcemekanism.

![bild](https://github.com/FrankieKG/PasswordCracking/assets/91194975/d36c8c38-48dc-43de-9afa-10e9409f93e6)

## Designval och implementation

### GPU-baserad parallellarbetning
Applikationen använder sig av GPU:ns förmåga att snabbt bearbeta stora mängder data genom parallellarbete. Genom att använda OpenCL kan applikationen distribuera beräkningsuppgifter över GPU:ns många kärnor, vilket borde resulterar i en ökning av hastigheten jämfört med en CPU-baserad lösning.

### Algoritmer för hashning
Denna applikation implementerar två olika SHA-256-algoritmer:

  1. En C#-baserad SHA-256-implementation för att generera hashvärden av slumpmässigt genererade lösenord.
  2. En CL-baserad SHA-256-implementation, ansvarig för att hasha lösenordsgissningar och jämföra dessa hashvärden med tidigare genererade lösenordshashar.

### Datastruktur
![bild](https://github.com/FrankieKG/PasswordCracking/assets/91194975/a8bc0a8b-1301-4168-8f7d-065f03128bd8)

Applikationen använder en effektiv datastruktur för att hantera lösenordsgissningar och skicka dem till GPU:n för hashning och jämförelse. Här är en översikt över hur datastrukturen fungerar:
  1. Lösenordsgenerering och lagring: Lösenord genereras och lagras i en lista innan de skickas för hashning. Varje genererat lösenord hashades med SHA-256 och lagrades med dess motsvarande hashvärde för att användas som mål vid brute force-attacker.

          List<string> hashedPasswords = new List<string>();
              
          for (int i = 0; i < numberOfPasswords; i++)
          {
              string password = PasswordGenerator.GenerateComplexPassword(passWordLength);
              string hashed = PasswordHasher.HashPassword(password);
              hashedPasswords.Add(hashed);
          }
  2. Parallellisering med GPU: Lösenordsgissningar skickas till GPU:n för att hashas och jämföras med målhashvärdena. Detta sker med hjälp av OpenCL för att utnyttja GPU:ns parallella bearbetningsförmåga.

          BruteForceCracker bruteForceCracker = new BruteForceCracker(characterSet: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#$%^&*()", maxLength);
          
          foreach (var hashedPassword in hashedPasswords)
          {
              bruteForceCracker.CrackPasswordGPU(hashedPassword, maxLength);
          }

  3. Effektiv minneshantering: Datastrukturen är designad för att vara minneseffektiv och ge snabb åtkomst till lösenordsgissningar. Detta innebär att data lagras och bearbetas på ett sätt som minimerar överföringstiden mellan CPU och GPU.

Genom att kombinera CPU-baserad lösenordsgenerering och hashning med GPU-baserad lösenordsgissning och jämförelse, kan applikationen dra nytta av båda processorns resurser för att effektivt knäcka lösenord i en testmiljö.
![bild](https://github.com/FrankieKG/PasswordCracking/assets/91194975/395d67d5-c7f8-4656-961b-8cabcacbae2f)

# Licens
Detta projekt är licensierat under MIT-licensen


