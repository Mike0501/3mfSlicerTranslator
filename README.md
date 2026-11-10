# 3MF Translator

Convertitore locale per Windows. Prende la geometria e le impostazioni di stampa compatibili da un progetto 3MF e le combina con il profilo stampante di un progetto salvato dallo slicer di destinazione.

## Avvio

Apri `bin/Release/esito/3MFTranslator.exe`. Per ricompilare serve l'SDK .NET 10.

Il selettore **Italiano / English** in alto a destra cambia la lingua dell'app, inclusi il resoconto e i dettagli esito. La scelta viene salvata sul computer.

Accanto alle lingue trovi l’interruttore con sole e luna per cambiare tema nella finestra aperta e il punto interrogativo per aprire la guida rapida. L'app conserva il tema scelto.

```powershell
dotnet build -c Release -o bin/Release/esito
```

## Conversione

1. In 3MF Translator scegli il file scaricato. In **Impostazioni da importare dall’origine** puoi usare le impostazioni attive o scegliere un preset di processo incorporato, se presente. Per i campi assenti dal preset scelto vengono usate le impostazioni attive.
2. In **Destinazione**, usa **Riferimento 3MF…** per scegliere un progetto salvato con la stampante e i preset dello slicer da cui vuoi stampare, oppure **Profilo…** per scegliere un profilo già salvato. Premi **Traduci e salva…** e scegli un nuovo nome.
3. Se ci sono differenze nelle impostazioni di stampa, scegli **Usa impostazioni di origine**, **Mantieni impostazioni di destinazione** oppure **Rivedi manualmente…**. La revisione permette di filtrare per nome e scegliere i valori singolarmente. Le voci senza un valore di origine compatibile seguono la destinazione. **Annulla** interrompe la conversione.
4. Apri **Dettagli esito…** sotto il resoconto. Il pulsante è rosso se ci sono errori, giallo se ci sono solo avvisi, verde se la conversione è riuscita senza avvisi. Apri il risultato **come progetto** nello slicer di destinazione e controlla l'anteprima dopo lo slicing.

La famiglia Bambu Studio, OrcaSlicer e Anycubic Slicer Next è supportata come origine e destinazione. Sono supportati in ingresso anche i progetti PrusaSlicer nel formato 2.x e SuperSlicer con geometria e impostazioni incorporate. La destinazione richiede un ugello FFF. I progetti Bambu/Orca con più ugelli possono essere adattati se tutti gli ugelli di origine hanno lo stesso diametro di quello di destinazione.

Se il progetto ha più slot della destinazione, il traduttore compatta gli slot inutilizzati quando può verificare le assegnazioni. Il resoconto mostra la mappatura fra slot di origine e destinazione. I progetti con colori dipinti o sequenze per layer richiedono slot sufficienti a mantenere gli indici originali.

Per PrusaSlicer e SuperSlicer l'importazione copre progetti a un piatto e un filamento, inclusi volumi negativi, modificatori, istanze e impostazioni di processo compatibili. L'altezza variabile, gli eventi per layer e gli intervalli di altezza di questi formati vengono segnalati come esclusi. Le destinazioni usano il formato Bambu/Orca. Cura, il nuovo formato di progetto di PrusaSlicer 3 e i filamenti virtuali sono fuori da questa versione.

### Profili di destinazione

Usa **Crea profilo… → Aggiungi da 3MF…** per salvare un riferimento da qualsiasi slicer di destinazione supportato, anche prima di aprire un progetto di origine. La libreria conserva una copia del 3MF sotto `%LOCALAPPDATA%\3MFTranslator\references`. Durante una conversione puoi richiamarlo da **Profilo…**. Il traduttore può creare e usare questi profili anche su un PC senza lo slicer che ha generato il riferimento.

Ogni riferimento è un preset base della stampante. Selezionalo in libreria e premi **Personalizza…** per salvare una variante con altezza strato, pareti, riempimento sparso e supporti. Le varianti condividono il riferimento e possono essere modificate in seguito. Durante la conversione, **Mantieni impostazioni di destinazione** applica la variante scelta. La revisione manuale permette di decidere per singola voce.

I riferimenti aggiunti con una versione precedente continuano a usare il percorso salvato. I vecchi preset locali possono essere trasformati in riferimenti con **Converti in 3MF…** o al primo utilizzo. Per quel passaggio serve lo slicer originale con i preset ancora disponibili. Le conversioni successive usano il riferimento salvato in libreria.

Il file risultante conserva mesh, piatti, posizioni, colori e assegnazioni agli slot nel formato condiviso. Il traduttore crea un processo incorporato con le impostazioni compatibili di origine, tra cui altezza dei layer, pareti, infill, supporti, velocità e accelerazioni di processo. Il resoconto mostra l'altezza strato di base, la provenienza delle impostazioni e le personalizzazioni che ha rilevato ma non ha potuto trasferire. Stampante, limiti macchina, temperature, profili filamento e spurgo provengono dal progetto modello. Se cambia la dimensione del piatto, controlla l'ingombro nello slicer. Il G-code di origine viene rimosso e gli intervalli di altezza con impostazioni personalizzate sono segnalati come esclusi.

## Uso da riga di comando

```powershell
dotnet bin/Release/esito/3MFTranslator.dll convert source.3mf reference.3mf translated.3mf report.txt
```

Usa percorsi nuovi per risultato e resoconto. Il codice di uscita è 0 quando il file è stato salvato, 1 per un errore di conversione o scrittura e 2 per argomenti non validi.

Per selezionare un preset incorporato, aggiungi il suo percorso nell'archivio, per esempio `Metadata/process_settings_1.config`, dopo il percorso del resoconto.
