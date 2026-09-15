Add-Type -AssemblyName PresentationFramework

# Останавливаем ненужные процессы
"msedgewebview2.exe","Widgets.exe","WidgetService.exe","WebViewHost.exe" | ForEach-Object {
    Stop-Process -Name ($_ -replace '.exe','') -Force -ErrorAction SilentlyContinue 
}

$displayNameMap = @{}
$stackPanel = New-Object System.Windows.Controls.Grid
$appsPerColumn = 25

function UpdateSelectAllButtonText {
    $anyUnchecked = $false
    foreach ($child in $stackPanel.Children) {
        if (-not $child.IsChecked) {
            $anyUnchecked = $true
            break
        }
    }
    $selectAllButton.Content = if ($anyUnchecked) { 'Выделить все' } else { 'Снять все' }
}

function RefreshAppList {
    $previousSelection = @{}
    foreach ($child in $stackPanel.Children) {
        if ($child -is [System.Windows.Controls.CheckBox]) {
            $previousSelection[$child.Content.Text] = $child.IsChecked
        }
    }

    $stackPanel.Children.Clear()
    $stackPanel.RowDefinitions.Clear()
    $stackPanel.ColumnDefinitions.Clear()
    $displayNameMap.Clear()

    # --- Добавляем OneDrive и Remote Desktop ---
    if (Test-Path "$env:SystemRoot\System32\OneDriveSetup.exe") {
        $displayNameMap['OneDrive'] = @{ Name='OneDrive' }
    }

    if (Test-Path "$env:SystemRoot\System32\mstsc.exe") {
        $displayNameMap['Подключение к удаленному рабочему столу'] = @{ Name='RemoteDesktop' }
    }

    $selectedApps = Get-AppxPackage | Where-Object {
        -not $_.NonRemovable -and -not $_.IsFramework -and $_.Name -notin @('Microsoft.MicrosoftEdge.Stable', 'Microsoft.MicrosoftEdge')
    }

    $startApps = Get-StartApps

    foreach ($app in $selectedApps) {
        $startApp = $startApps | Where-Object { $_.AppID -like "*$($app.Name)*" } | Select-Object -First 1
        $displayName = if ($startApp) { $startApp.Name } else { $app.Name }
        $displayName = $displayName -replace '^(MicrosoftWindows|Microsoft)[.\s]+', ''
        if ($app.Name -like '*Edge.GameAssist*') { $displayName = 'GameAssist' }
        $displayNameMap[$displayName] = $app
    }
	
	    # --- Если приложений нет ---
    if ($displayNameMap.Count -eq 0) {
        $textBlock = New-Object System.Windows.Controls.TextBlock -Property @{
            Text = "Все приложения удалены"
            Foreground = [System.Windows.Media.Brushes]::White
            FontSize = 14
            FontFamily = "Tahoma"
            HorizontalAlignment = "Center"
            TextAlignment = "Center"
            Margin = "10"
        }
        $stackPanel.Children.Add($textBlock) > $null
        $selectAllButton.IsEnabled = $false
        $removeButton.IsEnabled = $false
        return
    } else {
        $selectAllButton.IsEnabled = $true
        $removeButton.IsEnabled = $true
    }

    $columnCount = [Math]::Ceiling($displayNameMap.Count / $appsPerColumn)
    $window.Width = $columnCount * 400

    for ($i = 0; $i -lt $columnCount; $i++) {
        $colDef = New-Object System.Windows.Controls.ColumnDefinition
        $colDef.Width = [System.Windows.GridLength]::new(300)
        $stackPanel.ColumnDefinitions.Add($colDef)
    }

    $row = 0
    $col = 0

    foreach ($appDisplayName in $displayNameMap.Keys | Sort-Object) {
        $checkBox = New-Object System.Windows.Controls.CheckBox -Property @{
            Margin = (New-Object System.Windows.Thickness(10,7,10,1))
            Foreground = [System.Windows.Media.Brushes]::White
            Background = [System.Windows.Media.Brushes]::Transparent
        }

        $checkBoxTemplateXaml = @"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' 
                 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' 
                 TargetType='CheckBox'>
  <BulletDecorator Background='Transparent'>
    <BulletDecorator.Bullet>
      <Grid Width='16' Height='16'>
        <Border x:Name='Border' CornerRadius='3' Background='Transparent' BorderBrush='White' BorderThickness='1'/>
        <Path x:Name='CheckMark' Visibility='Hidden' Stroke='White' StrokeThickness='2' Data='M2,8 L6,12 L14,4'/>
      </Grid>
    </BulletDecorator.Bullet>
    <ContentPresenter VerticalAlignment='Center' Margin='5,0,0,0'/>
  </BulletDecorator>
  <ControlTemplate.Triggers>
    <Trigger Property='IsChecked' Value='True'>
      <Setter TargetName='CheckMark' Property='Visibility' Value='Visible'/>
    </Trigger>
    <Trigger Property='IsEnabled' Value='False'>
      <Setter TargetName='Border' Property='BorderBrush' Value='Gray'/>
      <Setter TargetName='CheckMark' Property='Stroke' Value='Gray'/>
      <Setter Property='Foreground' Value='Gray'/>
    </Trigger>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter TargetName='Border' Property='Background' Value='#666666'/>
      <Setter Property='Background' Value='#666666'/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>
"@

        $stringReader = New-Object System.Xml.XmlTextReader ([System.IO.StringReader] $checkBoxTemplateXaml)
        $template = [System.Windows.Markup.XamlReader]::Load($stringReader)
        $checkBox.Template = $template

        $textBlock = New-Object System.Windows.Controls.TextBlock -Property @{
            Text = $appDisplayName
            TextWrapping = 'Wrap'
            TextTrimming = 'CharacterEllipsis'
            Width = 300
            MaxWidth = 330
            Foreground = [System.Windows.Media.Brushes]::White
        }
        $checkBox.Content = $textBlock

        $checkBox.Add_Checked({ UpdateSelectAllButtonText })
        $checkBox.Add_Unchecked({ UpdateSelectAllButtonText })

        [System.Windows.Controls.Grid]::SetColumn($checkBox, $col)
        [System.Windows.Controls.Grid]::SetRow($checkBox, $row)

        if ($stackPanel.RowDefinitions.Count -le $row) {
            $stackPanel.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition))
        }

        $stackPanel.Children.Add($checkBox) > $null

        $row++
        if ($row -ge $appsPerColumn) {
            $row = 0
            $col++
        }
    }

    # Восстанавливаем состояние чекбоксов
    foreach ($child in $stackPanel.Children) {
        $appName = $child.Content.Text
        if ($previousSelection.ContainsKey($appName)) {
            $child.IsChecked = $previousSelection[$appName]
        } else {
            $child.IsChecked = $true
        }
    }

    UpdateSelectAllButtonText
	
	# --- Центрируем окно после изменения размера ---
	$window.UpdateLayout()
	[System.Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([action]{
		$window.Left = ([System.Windows.SystemParameters]::PrimaryScreenWidth - $window.ActualWidth) / 2
		$window.Top  = ([System.Windows.SystemParameters]::PrimaryScreenHeight - $window.ActualHeight) / 2
	}, [System.Windows.Threading.DispatcherPriority]::Background)
}

# Цвета
$darkGrayColor = [System.Windows.Media.Color]::FromRgb(34,34,34)
$darkGrayBrush = New-Object System.Windows.Media.SolidColorBrush($darkGrayColor)

# Окно
$window = New-Object System.Windows.Window -Property @{
    Title = 'Выберите приложения для удаления'
    SizeToContent = 'WidthAndHeight'
    ResizeMode = 'NoResize'
    WindowStartupLocation = 'CenterScreen'
    Background = $darkGrayBrush
    Foreground = [System.Windows.Media.Brushes]::White
}

# Стиль кнопок
$buttonStyleXaml = @"
<Style TargetType='Button' xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>
  <Setter Property='Margin' Value='10'/>
  <Setter Property='Height' Value='30'/>
  <Setter Property='Width' Value='120'/>
  <Setter Property='HorizontalAlignment' Value='Center'/>
  <Setter Property='FontFamily' Value='Tahoma'/>
  <Setter Property='FontWeight' Value='Normal'/>
  <Setter Property='FontSize' Value='12'/>
  <Setter Property='Background' Value='#1A1A1A'/>
  <Setter Property='Foreground' Value='White'/>
  <Setter Property='BorderBrush' Value='White'/>
  <Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Template'>
    <Setter.Value>
      <ControlTemplate TargetType='Button'>
        <Border Background='{TemplateBinding Background}'
                BorderBrush='{TemplateBinding BorderBrush}'
                BorderThickness='{TemplateBinding BorderThickness}'
                CornerRadius='3'
                Padding='{TemplateBinding Padding}'>
          <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
        </Border>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
  <Style.Triggers>
    <Trigger Property='IsMouseOver' Value='True'>
      <Setter Property='Background' Value='#555555'/>
    </Trigger>
    <Trigger Property='IsEnabled' Value='False'>
      <Setter Property='Foreground' Value='Gray'/>
      <Setter Property='BorderBrush' Value='Gray'/>
      <Setter Property='Background' Value='#333333'/>
    </Trigger>
  </Style.Triggers>
</Style>
"@

$stringReader = New-Object System.Xml.XmlTextReader ([System.IO.StringReader] $buttonStyleXaml)
$buttonStyle = [System.Windows.Markup.XamlReader]::Load($stringReader)

$selectAllButton = New-Object System.Windows.Controls.Button
$selectAllButton.Style = $buttonStyle
$selectAllButton.Content = 'Выделить все'

$removeButton = New-Object System.Windows.Controls.Button
$removeButton.Style = $buttonStyle
$removeButton.Content = 'Удалить'

$closeButton = New-Object System.Windows.Controls.Button
$closeButton.Style = $buttonStyle
$closeButton.Content = 'Закрыть'

$buttonStackPanel = New-Object System.Windows.Controls.StackPanel -Property @{
    Orientation = 'Horizontal'
    HorizontalAlignment = 'Center'
    Margin = '0,20,0,0'
}

$selectAllButton.Add_Click({
    $allChecked = $true
    foreach ($child in $stackPanel.Children) {
        if (-not $child.IsChecked) { $allChecked = $false; break }
    }
    foreach ($child in $stackPanel.Children) {
        $child.IsChecked = -not $allChecked
    }
    UpdateSelectAllButtonText
})

$removeButton.Add_Click({
    $allChecked = $true
    foreach ($child in $stackPanel.Children) {
        if (-not $child.IsChecked) { $allChecked = $false; break }
    }

    $nsudo = Join-Path $PSScriptRoot "NSudoLG.exe"
	$Helper = Join-Path $PSScriptRoot "Helper.exe"

if ($allChecked) {
    $res = [System.Windows.MessageBox]::Show(
        "Вы уверены, что хотите удалить все приложения?", 
        "Подтверждение удаления", [System.Windows.MessageBoxButton]::YesNo)
    if ($res -ne [System.Windows.MessageBoxResult]::Yes) { return }
	$window.Close()
	Start-Process $Helper -ArgumentList '/Overlay "Удаление всех предустановленных приложений" /Font "Impact" /Size 40' -WindowStyle Hidden

    # Удаляем Appx-приложения
    Get-AppxPackage | Where-Object { $_.NonRemovable -eq $false } |
        ForEach-Object { Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction SilentlyContinue }

    # ---------- OneDrive ----------
    Stop-Process -Name OneDrive -Force -ErrorAction SilentlyContinue
    Start-Process "$env:SystemRoot\System32\OneDriveSetup.exe" -ArgumentList "/uninstall" -WindowStyle Hidden -Wait
	Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    foreach ($path in "$env:LocalAppData\OneDrive","$env:ProgramData\Microsoft OneDrive","$env:UserProfile\OneDrive","$env:LocalAppData\Microsoft\OneDrive") {
        Start-Process $nsudo -ArgumentList "-U:T -P:E -ShowWindowMode:Hide -Wait cmd.exe /c rd /s /q `"$path`"" -Wait
    }
    Get-ChildItem "$env:SystemRoot\WinSxS" -Filter "amd64_microsoft-windows-onedrive-setup*" -Directory |
        ForEach-Object { Start-Process $nsudo -ArgumentList "-U:T -P:E -ShowWindowMode:Hide -Wait cmd.exe /c rd /s /q `"$($_.FullName)`"" -Wait }
    foreach ($file in "OneDriveSetup.exe","OneDrive.ico") {
        Start-Process $nsudo -ArgumentList "-U:T -P:E -ShowWindowMode:Hide -Wait cmd.exe /c del /q `"$env:SystemRoot\System32\$file`"" -Wait
    }
    Remove-ItemProperty "HKCU:\Software\Microsoft\OneDrive" -Name * -ErrorAction SilentlyContinue
    Remove-ItemProperty "HKCU:\Software\Microsoft\OneDrive\Accounts" -Name * -ErrorAction SilentlyContinue

    # ---------- Remote Desktop ----------
    $mstsc = Start-Process "$env:SystemRoot\System32\mstsc.exe" -ArgumentList '/uninstall' -WindowStyle Hidden -PassThru
    if (!$mstsc.HasExited) { Stop-Process -Id $mstsc.Id -Force -ErrorAction SilentlyContinue }
	Start-Sleep 3
    Start-Process $nsudo -ArgumentList "-U:T -P:E -ShowWindowMode:Hide -Wait cmd.exe /c del /q `"$env:SystemRoot\System32\mstsc.exe`"" -Wait
	gci HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall | ?{ $_.PSChildName -like "mstsc-*" } | % { rm $_.PsPath -Recurse -Force }

	# ---------- Папки в Пуске ----------
	Remove-Item -Path "$env:AppData\Microsoft\Windows\Start Menu\Programs\Accessibility" -Recurse -Force -ErrorAction SilentlyContinue
	Remove-Item -Path "$env:ProgramData\Microsoft\Windows\Start Menu\Programs\Accessories" -Recurse -Force -ErrorAction SilentlyContinue
	
	# ---------- Отключение службы AppX ----------
	Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Services\AppXSvc" Start  4
	
	# ---------- Перезапуск Проводника ----------
	Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
	Start-Sleep 1
	Start-Process explorer.exe -ArgumentList "/root," -WindowStyle Hidden
	Start-Process $Helper -ArgumentList "/Overlay"
    return
}

    # Удаляем выбранные
    $selectedAppsToDelete = @()
    foreach ($child in $stackPanel.Children) {
        if ($child.IsChecked) {
            $name = $child.Content.Text
            if ($displayNameMap.ContainsKey($name)) {
                $selectedAppsToDelete += $displayNameMap[$name]
            }
        }
    }

    if ($selectedAppsToDelete.Count -eq 0) {
        [System.Windows.MessageBox]::Show('Выберите хотя бы одно приложение')
        return
    }

    $res = [System.Windows.MessageBox]::Show(
        "Вы уверены, что хотите удалить выбранные приложения?", 
        "Подтверждение удаления", [System.Windows.MessageBoxButton]::YesNo)
    if ($res -ne [System.Windows.MessageBoxResult]::Yes) { return }

    $window.Close()
    Start-Process $Helper -ArgumentList '/Overlay "Удаление" /Font "Impact" /Size 40' -WindowStyle Hidden

    foreach ($app in $selectedAppsToDelete) {
        if ($app -is [hashtable] -and $app.Name -eq 'OneDrive') {
            Stop-Process -Name OneDrive -Force -ErrorAction SilentlyContinue
            Start-Process -FilePath "$env:SystemRoot\System32\OneDriveSetup.exe" -ArgumentList "/uninstall" -WindowStyle Hidden -Wait
			Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
            foreach ($path in @("$env:LocalAppData\OneDrive","$env:ProgramData\Microsoft OneDrive","$env:UserProfile\OneDrive","$env:LocalAppData\Microsoft\OneDrive")) {
                Start-Process -FilePath $nsudo -ArgumentList "-U:T -P:E -ShowWindowMode:Hide -Wait cmd.exe /c rd /s /q `"$path`"" -WindowStyle Hidden -Wait
            }
            foreach ($dir in Get-ChildItem -Path "$env:SystemRoot\WinSxS" -Filter "amd64_microsoft-windows-onedrive-setup*" -Directory) {
                Start-Process -FilePath $nsudo -ArgumentList "-U:T -P:E -ShowWindowMode:Hide -Wait cmd.exe /c rd /s /q `"$($dir.FullName)`"" -Wait
            }
            foreach ($file in @("OneDriveSetup.exe","OneDrive.ico")) {
                Start-Process -FilePath $nsudo -ArgumentList "-U:T -P:E -ShowWindowMode:Hide -Wait cmd.exe /c del /q `"$env:SystemRoot\System32\$file`"" -WindowStyle Hidden -Wait
            }
            Remove-ItemProperty -Path "HKCU:\Software\Microsoft\OneDrive" -Name * -ErrorAction SilentlyContinue
            Remove-ItemProperty -Path "HKCU:\Software\Microsoft\OneDrive\Accounts" -Name * -ErrorAction SilentlyContinue
        }
        elseif ($app -is [hashtable] -and $app.Name -eq 'RemoteDesktop') {
			$mstsc = Start-Process "$env:SystemRoot\System32\mstsc.exe" -ArgumentList '/uninstall' -WindowStyle Hidden -PassThru
			if (!$mstsc.HasExited) { Stop-Process -Id $mstsc.Id -Force -ErrorAction SilentlyContinue }
			Start-Sleep 3
			Start-Process $nsudo -ArgumentList "-U:T -P:E -ShowWindowMode:Hide -Wait cmd.exe /c del /q `"$env:SystemRoot\System32\mstsc.exe`"" -Wait
			gci HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall | ?{ $_.PSChildName -like "mstsc-*" } | % { rm $_.PsPath -Recurse -Force }
        }
        else {
            Remove-AppxPackage -Package $app.PackageFullName -AllUsers -ErrorAction SilentlyContinue
        }
    }
    Start-Process $Helper -ArgumentList "/Overlay"
})

$closeButton.Add_Click({ $window.Close() })

$buttonStackPanel.Children.Add($selectAllButton) > $null
$buttonStackPanel.Children.Add($removeButton) > $null
$buttonStackPanel.Children.Add($closeButton) > $null

$mainStack = New-Object System.Windows.Controls.StackPanel
$mainStack.Children.Add($stackPanel) > $null
$mainStack.Children.Add($buttonStackPanel) > $null

$window.Content = $mainStack

RefreshAppList
$window.ShowDialog() | Out-Null
