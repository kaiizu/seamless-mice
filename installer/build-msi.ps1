param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path $root 'obj\installer'
$productVersion = '0.2.3'
$fileVersion = '0.2.3.0'
$output = Join-Path $root ('dist\SeamlessMice-' + $productVersion + '-x64.msi')
New-Item -ItemType Directory -Force -Path $work,(Split-Path $output -Parent) | Out-Null
if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output }
$installer = New-Object -ComObject WindowsInstaller.Installer
function Call($object, [string]$method, [object[]]$arguments = @()) {
    try {
        switch ($method) {
            'OpenDatabase' { return $object.OpenDatabase([string]$arguments[0], [int]$arguments[1]) }
            'Import' { return $object.Import([string]$arguments[0], [string]$arguments[1]) }
            'OpenView' { return $object.OpenView([string]$arguments[0]) }
            'CreateRecord' { return $object.CreateRecord([int]$arguments[0]) }
            'SetStream' { return $object.SetStream([int]$arguments[0], [string]$arguments[1]) }
            'Execute' { if ($arguments.Count -gt 0) { return $object.Execute($arguments[0]) } else { return $object.Execute() } }
            'Close' { return $object.Close() }
            'Persist' { return $object.Persist() }
            'Commit' { return $object.Commit() }
            default { throw ('Unsupported MSI authoring operation: ' + $method) }
        }
    }
    catch { throw ($method + ': ' + $_.Exception.Message) }
}
function Property($object, [string]$name, [object[]]$arguments) {
    $object.GetType().InvokeMember($name, [Reflection.BindingFlags]::GetProperty, $null, $object, [object[]]@([int]$arguments[0]))
}
function SetProperty($object, [string]$name, [object[]]$arguments) {
    $value = if ($arguments[1] -is [int]) { [int]$arguments[1] } else { [string]$arguments[1] }
    $object.GetType().InvokeMember($name, [Reflection.BindingFlags]::SetProperty, $null, $object, [object[]]@([int]$arguments[0],$value)) | Out-Null
}
$database = Call $installer 'OpenDatabase' @($output, 3)
$schemas = @{}
function Table([string]$name, [string[]]$columns, [string[]]$types, [string[]]$keys, [object[]]$rows) {
    $script:schemas[$name] = @{ Columns=$columns; Types=$types; Keys=$keys }
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add(($columns -join "`t")); $lines.Add(($types -join "`t")); $lines.Add((@($name) + $keys -join "`t"))
    foreach ($row in $rows) { $lines.Add(($row -join "`t")) }
    [IO.File]::WriteAllLines((Join-Path $work ($name + '.idt')), $lines, [Text.Encoding]::ASCII)
    Call $database 'Import' @($work, ($name + '.idt')) | Out-Null
}
function BinaryRow([string]$table, [string]$id, [string]$path) {
    $sql = 'INSERT INTO `' + $table + '` (`Name`,`Data`) VALUES (?, ?)'
    $view = Call $database 'OpenView' @($sql)
    $record = Call $installer 'CreateRecord' @(2)
    SetProperty $record 'StringData' @(1, $id)
    Call $record 'SetStream' @(2, $path) | Out-Null
    Call $view 'Execute' @($record) | Out-Null; Call $view 'Close' | Out-Null
}
$productCode = '{7C906C6D-F746-42B0-910C-E767F9937177}'
$upgradeCode = '{DAC5B2BF-9C45-468A-A7F3-AE78D1CC693D}'
$properties = @(
    @('ProductCode',$productCode),@('UpgradeCode',$upgradeCode),@('ProductName','Seamless Mice'),@('ProductVersion',$productVersion),
    @('ProductLanguage','1033'),@('Manufacturer','Seamless Mice'),@('ALLUSERS','1'),@('INSTALLLEVEL','1'),
    @('ARPPRODUCTICON','SeamlessMice.exe'),@('ARPNOMODIFY','1'),
    @('SecureCustomProperties','SM_OLDPRODUCTS;SM_NEWERPRODUCTS;INSTALLFOLDER;SM_WINDOWS_MAJOR'),@('DefaultUIFont','Normal'),@('ErrorDialog','MessageDlg'),@('MSIRESTARTMANAGERCONTROL','DisableShutdown')
)
Table 'Property' @('Property','Value') @('s72','l0') @('Property') $properties
Table 'Directory' @('Directory','Directory_Parent','DefaultDir') @('s72','S72','l255') @('Directory') @(
    @('TARGETDIR','','SourceDir'),@('ProgramFiles64Folder','TARGETDIR','.'),@('INSTALLFOLDER','ProgramFiles64Folder','SMICE~1|Seamless Mice'),
    @('ProgramMenuFolder','TARGETDIR','.'),@('MenuDir','ProgramMenuFolder','SMICE~1|Seamless Mice'),@('System64Folder','TARGETDIR','.')
)
$payload = @(
    @('Engine','SeamlessMice.exe','bin\SeamlessMice.exe',$fileVersion),
    @('Settings','SeamlessMice.Settings.exe','bin\SeamlessMice.Settings.exe',$fileVersion),
    @('Core','SeamlessMice.Core.dll','bin\SeamlessMice.Core.dll',$fileVersion),
    @('Marker','installed.flag','installer\installed.flag',''),@('License','LICENSE','LICENSE',''),
    @('Icon','app.ico','assets\app.ico','')
)
if ((Get-FileHash -LiteralPath (Join-Path $root 'bin\SeamlessMice.exe')).Hash -ne (Get-FileHash -LiteralPath (Join-Path $root 'bin\SeamlessMice.Native.exe')).Hash) { throw 'Build the native engine before creating the installer.' }
$components = @(); $files = @(); $featureComponents = @(); $sequence = 0
$ddf = @('.OPTION EXPLICIT','.Set Cabinet=on','.Set Compress=on','.Set CompressionType=MSZIP','.Set CabinetNameTemplate=payload.cab',('.Set DiskDirectoryTemplate="' + $work + '"'),('.Set RptFileName="' + (Join-Path $work 'cabinet.rpt') + '"'),('.Set InfFileName="' + (Join-Path $work 'cabinet.inf') + '"'))
foreach ($item in $payload) {
    $sequence++; $id = [string]$item[0]; $path = Join-Path $root $item[2]
    # Stable, project-specific component IDs derived from their immutable file identities.
    $hash = [Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes('SeamlessMice.component.' + $id))
    $guidBytes = [byte[]]$hash[0..15]; $guid = ([Guid]::new($guidBytes)).ToString('B').ToUpperInvariant()
    $components += ,@(('C_' + $id),$guid,'INSTALLFOLDER',256,'',('F_' + $id))
    $featureComponents += ,@('Complete',('C_' + $id))
    $short = switch ($id) { 'Engine' { 'SMICE.EXE' } 'Settings' { 'SMSET.EXE' } 'Core' { 'SMCORE.DLL' } 'Marker' { 'INSTALL.FLG' } default { $item[1] } }
    $fileName = if ($short -eq $item[1]) { $short } else { $short + '|' + $item[1] }
    $language = if ($id -eq 'Engine') { '1033' } elseif ($item[3]) { '0' } else { '' }
    $files += ,@(('F_' + $id),('C_' + $id),$fileName,(Get-Item -LiteralPath $path).Length,$item[3],$language,0,$sequence)
    $ddf += '"' + $path + '" F_' + $id
}
[IO.File]::WriteAllLines((Join-Path $work 'payload.ddf'), $ddf, [Text.Encoding]::ASCII)
& (Join-Path $env:WINDIR 'System32\makecab.exe') /F (Join-Path $work 'payload.ddf') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Cabinet compression failed.' }
Table 'Component' @('Component','ComponentId','Directory_','Attributes','Condition','KeyPath') @('s72','S38','s72','i2','S255','S72') @('Component') $components
Table 'Feature' @('Feature','Feature_Parent','Title','Description','Display','Level','Directory_','Attributes') @('s38','S38','L64','L255','I2','i2','S72','i2') @('Feature') @(,@('Complete','','Seamless Mice','Native cursor engine and display layout editor',1,1,'INSTALLFOLDER',0))
Table 'FeatureComponents' @('Feature_','Component_') @('s38','s72') @('Feature_','Component_') $featureComponents
Table 'File' @('File','Component_','FileName','FileSize','Version','Language','Attributes','Sequence') @('s72','s72','l255','i4','S72','S20','I2','i2') @('File') $files
Table 'Media' @('DiskId','LastSequence','DiskPrompt','Cabinet','VolumeLabel','Source') @('i2','i2','L64','S255','S32','S72') @('DiskId') @(,@(1,$sequence,'','#payload.cab','',''))
BinaryRow '_Streams' 'payload.cab' (Join-Path $work 'payload.cab')
Table 'Icon' @('Name','Data') @('s72','v0') @('Name') @()
BinaryRow 'Icon' 'SeamlessMice.exe' (Join-Path $root 'assets\app.ico')
Table 'Binary' @('Name','Data') @('s72','v0') @('Name') @()
BinaryRow 'Binary' 'SetupLogo' (Join-Path $root 'assets\app.ico')
Table 'CustomAction' @('Action','Type','Source','Target') @('s72','i2','S64','S255') @('Action') @(,@('SetInstallLocation',51,'ARPINSTALLLOCATION','[INSTALLFOLDER]'))
Table 'Shortcut' @('Shortcut','Directory_','Name','Component_','Target','Arguments','Description','Hotkey','Icon_','IconIndex','ShowCmd','WkDir','DisplayResourceDLL','DisplayResourceId','DescriptionResourceDLL','DescriptionResourceId') @('s72','s72','l128','s72','s72','S255','L255','I2','S72','I2','I2','S72','S255','I2','S255','I2') @('Shortcut') @(
    ,@('Launch','MenuDir','SMICE|Seamless Mice','C_Engine','Complete','','Arrange displays and correct cursor transitions','','SeamlessMice.exe',0,1,'INSTALLFOLDER','','','','')
)
Table 'RemoveFile' @('FileKey','Component_','FileName','DirProperty','InstallMode') @('s72','s72','L255','s72','i2') @('FileKey') @(,@('RemoveMenu','C_Engine','','MenuDir',2))
Table 'Upgrade' @('UpgradeCode','VersionMin','VersionMax','Language','Attributes','Remove','ActionProperty') @('s38','S20','S20','S255','i4','S255','s72') @('UpgradeCode','VersionMin','VersionMax','Language','Attributes') @(
    @($upgradeCode,'0.0.0',$productVersion,'',256,'ALL','SM_OLDPRODUCTS'),@($upgradeCode,$productVersion,'','',2,'','SM_NEWERPRODUCTS')
)
# Windows Installer can expose VersionNT=603 on Windows 10/11. Search the actual
# 64-bit OS registry instead of treating that compatibility value as kernel version 10.
Table 'Signature' @('Signature','FileName','MinVersion','MaxVersion','MinSize','MaxSize','MinDate','MaxDate','Languages') @('s72','l255','S20','S20','I4','I4','I4','I4','S255') @('Signature') @()
Table 'AppSearch' @('Property','Signature_') @('s72','s72') @('Property','Signature_') @(,@('SM_WINDOWS_MAJOR','WindowsMajor'))
Table 'RegLocator' @('Signature_','Root','Key','Name','Type') @('s72','i2','s255','L255','I2') @('Signature_') @(,@('WindowsMajor',2,'SOFTWARE\Microsoft\Windows NT\CurrentVersion','CurrentMajorVersionNumber',18))
Table 'LaunchCondition' @('Condition','Description') @('s255','l255') @('Condition') @(
    @('VersionNT64 AND SM_WINDOWS_MAJOR = "#10"','Seamless Mice requires a 64-bit Windows 10 or Windows 11 installation.'),
    @('NOT SM_NEWERPRODUCTS OR Installed','A newer version of Seamless Mice is already installed.')
)
$execute = @(
    @('FindRelatedProducts','',25),@('AppSearch','',50),@('LaunchConditions','',100),@('CostInitialize','',800),@('FileCost','',900),@('CostFinalize','',1000),@('SetInstallLocation','',1100),@('MigrateFeatureStates','',1200),
    @('InstallValidate','',1400),@('InstallInitialize','',1500),@('RemoveExistingProducts','SM_OLDPRODUCTS',1550),@('ProcessComponents','',1600),@('UnpublishFeatures','',1800),
    @('RemoveShortcuts','',3200),@('RemoveFiles','',3500),@('RemoveFolders','',3600),@('CreateFolders','',3700),@('InstallFiles','',4000),@('CreateShortcuts','',4500),
    @('RegisterUser','',6000),@('RegisterProduct','',6100),@('PublishFeatures','',6300),@('PublishProduct','',6400),@('InstallFinalize','',6600)
)
Table 'InstallExecuteSequence' @('Action','Condition','Sequence') @('s72','S255','I2') @('Action') $execute
Table 'InstallUISequence' @('Action','Condition','Sequence') @('s72','S255','I2') @('Action') @(
    @('FindRelatedProducts','',25),@('AppSearch','',50),@('LaunchConditions','',100),@('CostInitialize','',800),@('FileCost','',900),@('CostFinalize','',1000),
    @('SetupDlg','NOT Installed',1230),@('MaintenanceDlg','Installed',1231),@('ExecuteAction','',1300),@('ExitDlg','',-1),@('CancelDlg','',-2),@('ErrorDlg','',-3)
)
Table 'AdminExecuteSequence' @('Action','Condition','Sequence') @('s72','S255','I2') @('Action') @(
    @('CostInitialize','',800),@('FileCost','',900),@('CostFinalize','',1000),@('InstallValidate','',1400),@('InstallInitialize','',1500),@('InstallAdminPackage','',3900),@('InstallFiles','',4000),@('InstallFinalize','',6600)
)
Table 'TextStyle' @('TextStyle','FaceName','Size','Color','StyleBits') @('s72','l32','i2','I4','I2') @('TextStyle') @(@('Normal','Segoe UI',9,0,0),@('Title','Segoe UI',16,0,0))
$dialogs = @(); $controls = @(); $events = @()
function Control([string]$dialog,[string]$name,[string]$type,[int]$x,[int]$y,[int]$w,[int]$h,[int]$attributes,[string]$property,[string]$text,[string]$next) {
    $script:controls += ,@($dialog,$name,$type,$x,$y,$w,$h,$attributes,$property,$text,$next,'')
}
function Event([string]$dialog,[string]$button,[string]$name,[string]$argument,[int]$order) { $script:events += ,@($dialog,$button,$name,$argument,'1',$order) }
$dialogs += ,@('SetupDlg',50,50,390,270,3,'Seamless Mice Setup','Install','Install','Cancel')
Control 'SetupDlg' 'Logo' 'Icon' 24 18 30 30 3 '' 'SetupLogo' ''
Control 'SetupDlg' 'Title' 'Text' 68 20 296 28 3 '' '{\Title}Install Seamless Mice' ''
Control 'SetupDlg' 'Body' 'Text' 24 62 340 46 3 '' 'A small native utility for cursor movement between displays of different sizes. Settings are saved separately for each Windows user.' ''
Control 'SetupDlg' 'PathLabel' 'Text' 24 120 340 16 3 '' 'Installation folder' ''
Control 'SetupDlg' 'Path' 'PathEdit' 24 140 340 20 3 'INSTALLFOLDER' '' 'Install'
Control 'SetupDlg' 'Note' 'Text' 24 180 340 34 3 '' 'Close the portable version before installing. Startup at sign-in can be enabled later in Settings.' ''
Control 'SetupDlg' 'Install' 'PushButton' 216 232 70 24 3 '' 'Install' 'Cancel'
Control 'SetupDlg' 'Cancel' 'PushButton' 294 232 70 24 3 '' 'Cancel' 'Path'
Event 'SetupDlg' 'Install' 'SetTargetPath' 'INSTALLFOLDER' 1
Event 'SetupDlg' 'Install' 'EndDialog' 'Return' 2
Event 'SetupDlg' 'Cancel' 'EndDialog' 'Exit' 1
$dialogs += ,@('MaintenanceDlg',50,50,390,210,3,'Seamless Mice Setup','Repair','Repair','Cancel')
Control 'MaintenanceDlg' 'Title' 'Text' 24 20 340 28 3 '' '{\Title}Maintain Seamless Mice' ''
Control 'MaintenanceDlg' 'Body' 'Text' 24 65 340 60 3 '' 'Repair the installed program or remove it. Your per-user calibration files are retained when the program is removed. Disable Start with Windows in Settings before uninstalling.' ''
Control 'MaintenanceDlg' 'Repair' 'PushButton' 112 160 78 24 3 '' 'Repair' 'Remove'
Control 'MaintenanceDlg' 'Remove' 'PushButton' 198 160 78 24 3 '' 'Uninstall' 'Cancel'
Control 'MaintenanceDlg' 'Cancel' 'PushButton' 284 160 78 24 3 '' 'Cancel' 'Repair'
Event 'MaintenanceDlg' 'Repair' '[REINSTALL]' 'ALL' 1
Event 'MaintenanceDlg' 'Repair' '[REINSTALLMODE]' 'omus' 2
Event 'MaintenanceDlg' 'Repair' 'EndDialog' 'Return' 3
Event 'MaintenanceDlg' 'Remove' '[REMOVE]' 'ALL' 1
Event 'MaintenanceDlg' 'Remove' 'EndDialog' 'Return' 2
Event 'MaintenanceDlg' 'Cancel' 'EndDialog' 'Exit' 1
foreach ($definition in @(@('ExitDlg','Setup complete','Use the Start menu to open Seamless Mice. Choose your layout and click Apply. You can import a calibration file from the portable version.'),@('CancelDlg','Setup cancelled','The setup was cancelled.'),@('ErrorDlg','Setup did not finish','Windows Installer could not complete the operation. Check the installation log for details.'))) {
    $name = $definition[0]; $dialogs += ,@($name,50,50,390,210,3,'Seamless Mice Setup','Finish','Finish','Finish')
    Control $name 'Title' 'Text' 24 20 340 28 3 '' ('{\Title}' + $definition[1]) ''
    Control $name 'Body' 'Text' 24 65 340 70 3 '' $definition[2] ''
    Control $name 'Finish' 'PushButton' 284 160 78 24 3 '' 'Close' 'Finish'
    Event $name 'Finish' 'EndDialog' 'Return' 1
}
$dialogs += ,@('MessageDlg',50,50,390,210,65539,'Seamless Mice Setup','ErrorText','O','C')
Control 'MessageDlg' 'ErrorIcon' 'Icon' 24 24 28 28 1048579 '' 'SetupLogo' ''
Control 'MessageDlg' 'ErrorText' 'Text' 64 24 300 105 3 '' '' ''
$errorButtons = @(@('A','Abort','ErrorAbort'),@('C','Cancel','ErrorCancel'),@('I','Ignore','ErrorIgnore'),@('N','No','ErrorNo'),@('O','OK','ErrorOk'),@('R','Retry','ErrorRetry'),@('Y','Yes','ErrorYes'))
for ($index=0;$index -lt $errorButtons.Count;$index++) {
    # Windows creates only the buttons needed for a particular message. The engine
    # owns their focus order; authored next-control links can point to absent buttons.
    $button=$errorButtons[$index]
    Control 'MessageDlg' $button[0] 'PushButton' 24 160 70 24 3 '' $button[1] ''
    Event 'MessageDlg' $button[0] 'EndDialog' $button[2] 1
}
$dialogs += ,@('FilesInUse',50,50,390,260,3,'Files in use','Retry','Retry','Cancel')
Control 'FilesInUse' 'Title' 'Text' 24 20 340 28 3 '' '{\Title}Close the running application' ''
Control 'FilesInUse' 'Body' 'Text' 24 58 340 38 3 '' 'Close the listed applications, then click Retry to continue.' ''
Control 'FilesInUse' 'List' 'ListBox' 24 104 340 94 3 'FileInUseProcess' '' 'Retry'
Control 'FilesInUse' 'Retry' 'PushButton' 132 220 70 24 3 '' 'Retry' 'Ignore'
Control 'FilesInUse' 'Ignore' 'PushButton' 210 220 70 24 3 '' 'Ignore' 'Cancel'
Control 'FilesInUse' 'Cancel' 'PushButton' 288 220 70 24 3 '' 'Cancel' 'List'
Event 'FilesInUse' 'Retry' 'EndDialog' 'Retry' 1
Event 'FilesInUse' 'Ignore' 'EndDialog' 'Ignore' 1
Event 'FilesInUse' 'Cancel' 'EndDialog' 'Exit' 1
Table 'ListBox' @('Property','Order','Value','Text') @('s72','i2','s64','L64') @('Property','Order') @()
Table 'Dialog' @('Dialog','HCentering','VCentering','Width','Height','Attributes','Title','Control_First','Control_Default','Control_Cancel') @('s72','i2','i2','i2','i2','I4','L128','s50','S50','S50') @('Dialog') $dialogs
Table 'Control' @('Dialog_','Control','Type','X','Y','Width','Height','Attributes','Property','Text','Control_Next','Help') @('s72','s50','s20','i2','i2','i2','i2','I4','S50','L0','S50','L50') @('Dialog_','Control') $controls
Table 'ControlEvent' @('Dialog_','Control_','Event','Argument','Condition','Ordering') @('s72','s50','s50','s255','s255','I2') @('Dialog_','Control_','Event','Argument','Condition') $events
# The Windows SDK sample package supplies Microsoft's standard MSI column validation
# metadata. Import it so the SDK ICEs can validate foreign keys, ranges and categories.
$sdkBin = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$schemaPackage = Get-ChildItem -LiteralPath $sdkBin -Filter 'Orca-x86_en-us.msi' -Recurse | Sort-Object FullName -Descending | Select-Object -First 1
if ($null -eq $schemaPackage) { throw 'Windows SDK Orca schema package is required to validate the installer.' }
$schemaDatabase = $installer.OpenDatabase([string]$schemaPackage.FullName, 0)
$schemaDatabase.Export('_Validation',[string]$work,'_Validation.idt')
$cubePath = Join-Path $root 'obj\installer-tools\darice.cub'
if (-not (Test-Path -LiteralPath $cubePath)) {
    $sdkFiles = $schemaDatabase.OpenView('SELECT `File`,`Sequence` FROM `File` WHERE `FileName`=''darice.cub'''); $sdkFiles.Execute(); $cubeFile = $sdkFiles.Fetch()
    if ($null -eq $cubeFile) { throw 'Windows SDK validation cube was not found.' }
    $cubeId = $cubeFile.StringData(1); $cubeSequence = $cubeFile.IntegerData(2); $sdkFiles.Close()
    $mediaView = $schemaDatabase.OpenView('SELECT `LastSequence`,`Cabinet` FROM `Media` ORDER BY `DiskId`'); $mediaView.Execute(); $cabinet = $null
    while ($null -ne ($disk = $mediaView.Fetch())) { if ($disk.IntegerData(1) -ge $cubeSequence) { $cabinet = $disk.StringData(2); break } }; $mediaView.Close()
    if (-not $cabinet -or $cabinet.StartsWith('#')) { throw 'Expected an external Windows SDK cabinet.' }
    $toolFolder = Split-Path $cubePath -Parent; New-Item -ItemType Directory -Force -Path $toolFolder | Out-Null
    & (Join-Path $env:WINDIR 'System32\expand.exe') ('-F:' + $cubeId) (Join-Path $schemaPackage.DirectoryName $cabinet) $toolFolder | Out-Null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $toolFolder $cubeId))) { throw 'Could not extract the SDK validation cube.' }
    Copy-Item -LiteralPath (Join-Path $toolFolder $cubeId) -Destination $cubePath
}
$cubeMetadata = Join-Path $work 'cube-meta'
New-Item -ItemType Directory -Force -Path $cubeMetadata | Out-Null
$cubeDatabase = $installer.OpenDatabase([string]$cubePath,0)
$cubeDatabase.Export('_Validation',[string]$cubeMetadata,'_Validation.idt')
$standard = [IO.File]::ReadAllLines((Join-Path $work '_Validation.idt'))
$validationRows = @{}
foreach ($file in @((Join-Path $cubeMetadata '_Validation.idt'),(Join-Path $work '_Validation.idt'))) {
    foreach ($line in ([IO.File]::ReadAllLines($file) | Select-Object -Skip 3)) {
        $parts = $line.Split([char]9); if ($parts.Length -ge 2) { $validationRows[$parts[0] + ':' + $parts[1]] = $line }
    }
}
# Supply the authored UI/file table metadata absent from the SDK's small sample packages.
$categories = @{
    'Dialog:Dialog'='Identifier';'Dialog:Title'='Formatted';'Dialog:Control_First'='Identifier';'Dialog:Control_Default'='Identifier';'Dialog:Control_Cancel'='Identifier';
    'Control:Dialog_'='Identifier';'Control:Control'='Identifier';'Control:Type'='Identifier';'Control:Property'='Identifier';'Control:Text'='Formatted';'Control:Control_Next'='Identifier';
    'ControlEvent:Dialog_'='Identifier';'ControlEvent:Control_'='Identifier';'ControlEvent:Event'='Formatted';'ControlEvent:Argument'='Formatted';'ControlEvent:Condition'='Condition';
    'Icon:Name'='Identifier';'RemoveFile:FileKey'='Identifier';'RemoveFile:Component_'='Identifier';'RemoveFile:FileName'='WildCardFilename';'RemoveFile:DirProperty'='Identifier';
    'TextStyle:TextStyle'='Identifier';'ListBox:Property'='Identifier';'ListBox:Value'='Formatted';'ListBox:Text'='Formatted'
}
foreach ($tableName in $schemas.Keys) {
    $schema=$schemas[$tableName]
    for ($columnIndex=0;$columnIndex -lt $schema.Columns.Count;$columnIndex++) {
        $column=$schema.Columns[$columnIndex];$key=$tableName+':'+$column
        if ($validationRows.ContainsKey($key)) { continue }
        $type=$schema.Types[$columnIndex];$nullable=if([char]::IsUpper($type[0])){'Y'}else{'N'}
        $category=if($type[0] -in @('i','I')){''}elseif($type[0] -eq 'v'){'Binary'}elseif($categories.ContainsKey($key)){$categories[$key]}else{'Text'}
        $keyTable='';$keyColumn=''
        if($key -in @('Control:Dialog_','ControlEvent:Dialog_')){$keyTable='Dialog';$keyColumn='1'}
        if($key -eq 'RemoveFile:Component_'){$keyTable='Component';$keyColumn='1'}
        $validationRows[$key]=@($tableName,$column,$nullable,'','',$keyTable,$keyColumn,$category,'',('Authored '+$column+' column')) -join "`t"
    }
}
$validationRows.Remove('CustomAction:ExtendedType')
[IO.File]::WriteAllLines((Join-Path $work '_Validation.idt'),[string[]](@($standard[0..2]) + @($validationRows.Values)),[Text.Encoding]::ASCII)
Call $database 'Import' @($work,'_Validation.idt') | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($cubeDatabase) | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($schemaDatabase) | Out-Null
$summary = Property $database 'SummaryInformation' @(20)
foreach ($pair in @(@(1,1252),@(2,'Seamless Mice Setup'),@(3,'Seamless Mice native x64 installer'),@(4,'Seamless Mice'),@(7,'x64;1033'),@(9,([Guid]::NewGuid().ToString('B').ToUpperInvariant())),@(14,500),@(15,2),@(18,'Seamless Mice MSI builder'),@(19,2))) { SetProperty $summary 'Property' @($pair[0],$pair[1]) }
Call $summary 'Persist' | Out-Null; Call $database 'Commit' | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) | Out-Null
[Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
Write-Output ('Built ' + $output)
