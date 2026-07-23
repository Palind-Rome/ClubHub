param(
    [string]$ConfigurationPath = (Join-Path $PSScriptRoot '..\backend\appsettings.Development.json'),
    [ValidateRange(2, 32)]
    [int]$Workers = 8,
    [ValidateRange(1, 1000)]
    [int]$ValuesPerWorker = 16,
    [switch]$ConfirmIsolatedDatabase
)

$ErrorActionPreference = 'Stop'

if (-not $ConfirmIsolatedDatabase)
{
    throw @'
并发验证会推进 Sequence，只允许连接隔离测试 Schema 或一次性数据库。
确认目标数据库满足隔离要求后，请显式传入 -ConfirmIsolatedDatabase。
'@
}

$sequenceTargets = @(
    [pscustomobject]@{ TableName = 'ROLES'; ColumnName = 'ROLE_ID'; SequenceName = 'SEQ_ROLES' },
    [pscustomobject]@{ TableName = 'RECRUITMENTS'; ColumnName = 'RECRUIT_ID'; SequenceName = 'SEQ_RECRUITMENTS' },
    [pscustomobject]@{ TableName = 'RECRUITMENT_APPLICATIONS'; ColumnName = 'APPLICATION_ID'; SequenceName = 'SEQ_RECRUITMENT_APPLICATIONS' },
    [pscustomobject]@{ TableName = 'VENUES'; ColumnName = 'VENUE_ID'; SequenceName = 'SEQ_VENUES' },
    [pscustomobject]@{ TableName = 'VENUE_RESERVATIONS'; ColumnName = 'RESERVATION_ID'; SequenceName = 'SEQ_VENUE_RESERVATIONS' },
    [pscustomobject]@{ TableName = 'PROJECTS'; ColumnName = 'PROJECT_ID'; SequenceName = 'SEQ_PROJECTS' },
    [pscustomobject]@{ TableName = 'PROJECT_MEMBERS'; ColumnName = 'PROJECT_MEMBER_ID'; SequenceName = 'SEQ_PROJECT_MEMBERS' },
    [pscustomobject]@{ TableName = 'PROJECT_TASKS'; ColumnName = 'TASK_ID'; SequenceName = 'SEQ_PROJECT_TASKS' },
    [pscustomobject]@{ TableName = 'PROJECT_TASK_ASSIGNEES'; ColumnName = 'TASK_ASSIGNEE_ID'; SequenceName = 'SEQ_PROJECT_TASK_ASSIGNEES' },
    [pscustomobject]@{ TableName = 'PROJECT_TASK_PROGRESS_REPORTS'; ColumnName = 'TASK_PROGRESS_REPORT_ID'; SequenceName = 'SEQ_PROJECT_TASK_PROGRESS_REPORTS' },
    [pscustomobject]@{ TableName = 'LEARNING_ITEMS'; ColumnName = 'ITEM_ID'; SequenceName = 'SEQ_LEARNING_ITEMS' },
    [pscustomobject]@{ TableName = 'LEARNING_RECORDS'; ColumnName = 'RECORD_ID'; SequenceName = 'SEQ_LEARNING_RECORDS' },
    [pscustomobject]@{ TableName = 'MATERIALS'; ColumnName = 'MATERIAL_ID'; SequenceName = 'SEQ_MATERIALS' },
    [pscustomobject]@{ TableName = 'MATERIAL_BORROWS'; ColumnName = 'BORROW_ID'; SequenceName = 'SEQ_MATERIAL_BORROWS' },
    [pscustomobject]@{ TableName = 'NOTICES'; ColumnName = 'NOTICE_ID'; SequenceName = 'SEQ_NOTICES' },
    [pscustomobject]@{ TableName = 'FORUM_POSTS'; ColumnName = 'POST_ID'; SequenceName = 'SEQ_FORUM_POSTS' },
    [pscustomobject]@{ TableName = 'OPERATION_LOGS'; ColumnName = 'LOG_ID'; SequenceName = 'SEQ_OPERATION_LOGS' }
)

$resolvedConfigurationPath = Resolve-Path -LiteralPath $ConfigurationPath -ErrorAction Stop
$configuration = Get-Content -Raw -LiteralPath $resolvedConfigurationPath | ConvertFrom-Json
$connectionString = [string]$configuration.ConnectionStrings.Default
if ([string]::IsNullOrWhiteSpace($connectionString))
{
    throw "配置文件缺少 ConnectionStrings.Default：$resolvedConfigurationPath"
}

$oracleAssembly = Get-ChildItem `
    -LiteralPath (Join-Path $env:USERPROFILE '.nuget\packages\oracle.manageddataaccess.core') `
    -Recurse `
    -Filter 'Oracle.ManagedDataAccess.dll' |
    Where-Object { $_.FullName -match '\\lib\\net8\.0\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if ($null -eq $oracleAssembly)
{
    throw '未找到 Oracle.ManagedDataAccess.dll，请先执行 dotnet restore。'
}
$oracleAssemblyPath = $oracleAssembly.FullName
[void][System.Reflection.Assembly]::LoadFrom($oracleAssemblyPath)

$preflightConnection = [Oracle.ManagedDataAccess.Client.OracleConnection]::new($connectionString)
try
{
    $preflightConnection.Open()
    $preflightCommand = $preflightConnection.CreateCommand()
    try
    {
        # USER_TAB_COLUMNS.DATA_DEFAULT is LONG in Oracle 18c, so fetch the full
        # value before checking that it references the expected sequence.
        $preflightCommand.InitialLONGFetchSize = -1

        foreach ($target in $sequenceTargets)
        {
            $tableName = $target.TableName
            $columnName = $target.ColumnName
            $sequenceName = $target.SequenceName

            $preflightCommand.CommandText =
                "SELECT NVL(MAX($columnName), 0) FROM $tableName"
            [long]$maxId = $preflightCommand.ExecuteScalar()

            $preflightCommand.CommandText =
                "SELECT LAST_NUMBER FROM USER_SEQUENCES WHERE SEQUENCE_NAME = '$sequenceName'"
            $lastNumberValue = $preflightCommand.ExecuteScalar()
            if ($null -eq $lastNumberValue -or $lastNumberValue -is [DBNull])
            {
                throw "$sequenceName 不存在，请先执行 Issue #150 的数据库迁移。"
            }

            [long]$lastNumber = $lastNumberValue
            if ($lastNumber -le $maxId)
            {
                throw "$sequenceName 落后于 $tableName.$columnName：LAST_NUMBER=$lastNumber，MAX=$maxId。"
            }

            $preflightCommand.CommandText =
                "SELECT DATA_DEFAULT FROM USER_TAB_COLUMNS " +
                "WHERE TABLE_NAME = '$tableName' AND COLUMN_NAME = '$columnName'"
            $defaultValue = $preflightCommand.ExecuteScalar()
            if ($null -eq $defaultValue -or $defaultValue -is [DBNull])
            {
                throw "$tableName.$columnName 尚未配置数据库默认值。"
            }

            $normalizedDefault =
                (([string]$defaultValue) -replace '\s', '').
                Replace('"', '').
                ToUpperInvariant()
            $expectedDefaultSuffix = "$sequenceName.NEXTVAL"
            if (-not $normalizedDefault.EndsWith(
                $expectedDefaultSuffix,
                [StringComparison]::Ordinal))
            {
                throw "$tableName.$columnName 默认值不是 $expectedDefaultSuffix：$normalizedDefault"
            }
        }
    }
    finally
    {
        $preflightCommand.Dispose()
    }
}
finally
{
    $preflightConnection.Dispose()
}

Write-Output "17 个 Sequence 的存在性、推进位置和列默认值检查通过。"

$expectedValueCount = $Workers * $ValuesPerWorker
foreach ($target in $sequenceTargets)
{
    $sequenceName = $target.SequenceName
    $values = 1..$Workers | ForEach-Object -Parallel {
        [void][System.Reflection.Assembly]::LoadFrom($using:oracleAssemblyPath)
        $connection = [Oracle.ManagedDataAccess.Client.OracleConnection]::new($using:connectionString)
        try
        {
            $connection.Open()
            $command = $connection.CreateCommand()
            try
            {
                $command.CommandText = "SELECT $using:sequenceName.NEXTVAL FROM DUAL"
                $command.CommandTimeout = 20
                for ($valueIndex = 0; $valueIndex -lt $using:ValuesPerWorker; $valueIndex++)
                {
                    [long]$command.ExecuteScalar()
                }
            }
            finally
            {
                $command.Dispose()
            }
        }
        finally
        {
            $connection.Dispose()
        }
    } -ThrottleLimit $Workers

    $distinctValueCount = @($values | Sort-Object -Unique).Count
    if ($values.Count -ne $expectedValueCount -or $distinctValueCount -ne $expectedValueCount)
    {
        throw "$sequenceName 并发取值失败：期望 $expectedValueCount 个唯一值，实际获得 $($values.Count) 个值、$distinctValueCount 个唯一值。"
    }

    Write-Output "$sequenceName OK：$expectedValueCount 个并发取值全部唯一。"
}

Write-Output "全部 $($sequenceTargets.Count) 个 Sequence 并发验证通过；未写入任何业务表。"
