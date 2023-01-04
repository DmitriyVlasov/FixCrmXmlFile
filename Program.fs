open Argu
open System
open System.IO
open System.IO.Compression
open System.Xml

type Arguments =
  | ZipFileName of path:string
  | NormilizeMoneyFormat
  interface IArgParserTemplate with
    member s.Usage =
        match s with
        | ZipFileName _ -> "Determine the path to the Zip archive."

type Parameters( args:string array ) = 
    let parser = ArgumentParser.Create<Arguments>(programName = "FixCrmXmlFile.exe")
    let results = parser.Parse args
    let zipFileName = results.GetResult (ZipFileName, defaultValue = "")
    member _.ZipFileName with get() = zipFileName
    member _.Correct with get() = not <| String.IsNullOrEmpty zipFileName
    member _.Usage with get() = parser.PrintUsage()

type TableSchema = {
  TableName : string
  Fields : string seq
}

let getDestinationArchiveFileName (sourceArchiveFileName:string) = 
  let baseDirectory = Path.GetDirectoryName(sourceArchiveFileName)
  let fileName = Path.GetFileNameWithoutExtension(sourceArchiveFileName)
  let timestampString = DateTime.Now.ToString("yyyy-MM-dd-HH-mm")
  let resultFileName = $"{fileName}-fixed-{timestampString}.zip"
  Path.Combine(baseDirectory, resultFileName)

let getTableSchemasForFix (filename:string) = 
  let docSchema = new XmlDocument()
  docSchema.Load(filename)
  let root = docSchema.FirstChild
  root.ChildNodes
  |> Seq.cast<XmlNode>
  |> Seq.map ( fun node -> 
      let name = node.Attributes["name"].Value
      let moneyFileds = 
        node["fields"]
        |> Seq.cast<XmlNode>
        |> Seq.filter (fun node -> node.Attributes["type"].Value = "money")
        |> Seq.map (fun node -> 
          node.Attributes["name"].Value)
      { 
        TableName = name
        Fields = moneyFileds 
      }
    )
    |> Seq.filter ( fun i -> i.Fields |> Seq.length > 0 )
  
let fixDataFile (filePath:string) (tableSchemas:TableSchema seq) =  
  let doc = new XmlDocument()
  doc.Load(filePath)

  doc.FirstChild.ChildNodes
  |> Seq.cast<XmlNode>
  |> Seq.filter (fun tableNode -> 
    let tableName = tableNode.Attributes["name"].Value
    tableSchemas |> Seq.exists ( fun table -> table.TableName = tableName)
    )
  |> Seq.iter ( fun tableNode -> 
    let tableName = tableNode.Attributes["name"].Value
    let fieldsForFix = 
      tableSchemas
      |> Seq.find ( fun item -> item.TableName = tableName)
      |> fun item -> item.Fields
    let recordNodes = tableNode["records"] |> Seq.cast<XmlNode>

    recordNodes
    |> Seq.iter ( fun recordNode -> 
      let fieldNodes = recordNode.ChildNodes |> Seq.cast<XmlNode>

      fieldNodes
      |> Seq.filter ( fun fieldNode -> 
        let fieldName = fieldNode.Attributes["name"].Value
        fieldsForFix |> Seq.exists ( fun fixName -> fixName = fieldName) 
        )
      |> Seq.iter ( fun fieldNode -> 
        let oldValue = fieldNode.Attributes["value"].Value
        let newValue = oldValue.Replace(",",".") 
        fieldNode.Attributes["value"].Value <- newValue
      )
    )
  )
  
  doc.Save(filePath)

let WORK_DIRECTORY_NAME = Path.Combine(
    Path.GetTempPath(),
    Path.GetRandomFileName()
  )

[<Literal>]
let SCHEMA_FILE_NAME = "data_schema.xml"

[<Literal>]
let DATA_FILE_NAME = "data.xml"

let deleteWorkDirectory() =
  try Directory.Delete(WORK_DIRECTORY_NAME, true) with _ -> ()

let getSchemaPath() = Path.Combine(WORK_DIRECTORY_NAME, SCHEMA_FILE_NAME) 
let getDataPath() = Path.Combine(WORK_DIRECTORY_NAME, DATA_FILE_NAME)

let extractZip sourceArchiveFileName = 
  ZipFile.ExtractToDirectory(sourceArchiveFileName, WORK_DIRECTORY_NAME, true)

let zipFile sourceArchiveFileName = 
  let destinationArchiveFileName = getDestinationArchiveFileName sourceArchiveFileName
  ZipFile.CreateFromDirectory( WORK_DIRECTORY_NAME, destinationArchiveFileName, CompressionLevel.Optimal, false )

let now() = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")

[<EntryPoint>]
let main argv = 
  let parameters = Parameters(argv)
  if not parameters.Correct 
  then 
    printfn "%s" parameters.Usage
    1
  else 
    let sourceArchiveFileName = parameters.ZipFileName

    printfn "%s | Start processing" (now())
    
    printfn "%s | Start unpacked the archive..." (now())
    extractZip sourceArchiveFileName

    printfn "%s | The archive unpacked." (now())
    
    printfn "%s | Start Fix Data File..." (now())

    getSchemaPath()
    |> getTableSchemasForFix
    |> fixDataFile ( getDataPath() )

    printfn "%s | Data File Fixed." (now())
    printfn "%s | Start Create Zip Archive..." (now())

    zipFile sourceArchiveFileName

    printfn "%s | Zip Archive Created." (now())

    deleteWorkDirectory()
    
    printfn "%s | Remove temp directory" (now())
    printfn "%s | Finish" (now())

    0