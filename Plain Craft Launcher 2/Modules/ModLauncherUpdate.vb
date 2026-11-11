Friend Module ModLauncherUpdate

    Friend Const LauncherReleasesUrl As String = "https://github.com/zeze49024/ZEPCL/releases"
    Private Const ReleasesApi As String = "https://api.github.com/repos/zeze49024/ZEPCL/releases"
    Private Const ReleaseTagPrefix As String = "zepcl-v"
    Private Const IncludeBetaReleases As Boolean = VersionBetaSuffix <> ""
    Private ReadOnly InstalledVersion As (Number As Version, Rank As Integer) =
        (New Version(VersionBaseName), If(IncludeBetaReleases, Integer.Parse(VersionBetaSuffix.Substring(5)), Integer.MaxValue))
    Private LatestStatus As Integer = -1
    Private Checking As Integer

    Friend Function IsLauncherNewest() As Boolean?
        Dim Status = Volatile.Read(LatestStatus)
        If Status < 0 Then Return Nothing
        Return Status = 1
    End Function

    Friend Sub UpdateCheckByButton()
        CheckLauncherUpdate(True)
    End Sub

    Friend Sub CheckLauncherUpdate(IsManual As Boolean)
        If IsProgramEnding OrElse (Not IsManual AndAlso Not Settings.Get(Of Boolean)("SystemZepclAutoUpdate")) Then Return
        If Interlocked.CompareExchange(Checking, 1, 0) <> 0 Then
            If IsManual Then Hint("正在检查启动器更新……")
            Return
        End If
        If IsManual Then Hint("正在检查启动器更新……")
        RunInNewThread(
            Sub()
                Try
                    Dim Latest = FetchLatestRelease()
                    If Latest.Number Is Nothing Then
                        Volatile.Write(LatestStatus, -1)
                        If IsManual Then Hint("当前渠道暂无可用的 ZEPCL 发布。")
                        Return
                    End If
                    Dim HasUpdate = Latest.CompareTo(InstalledVersion) > 0
                    Volatile.Write(LatestStatus, If(HasUpdate, 0, 1))
                    If HasUpdate Then
                        Dim LatestName = Latest.Number.ToString() & If(Latest.Rank = Integer.MaxValue, "", $"-beta{Latest.Rank}")
                        Dim Page = $"{LauncherReleasesUrl}/tag/{ReleaseTagPrefix}{LatestName}"
                        If MyMsgBox($"发现 ZEPCL 新版本 {LatestName}。当前版本为 {VersionName}。",
                                    "启动器更新", "前往下载", "稍后") = 1 Then OpenWebsite(Page)
                    ElseIf IsManual Then
                        Hint("当前启动器已是最新版本。", HintType.Green)
                    End If
                Catch ex As Exception
                    Logger.Warn(ex, "检查 ZEPCL 更新失败")
                    If IsManual Then Hint("检查更新失败，请稍后重试或访问 ZEPCL 的 GitHub 发布页。", HintType.Red)
                Finally
                    Interlocked.Exchange(Checking, 0)
                End Try
            End Sub, "ZEPCL Update Check", ThreadPriority.BelowNormal)
    End Sub

    Private Function FetchLatestRelease() As (Number As Version, Rank As Integer)
        Const PageSize As Integer = 100
        Dim Page As Integer = 1
        Dim Latest As (Number As Version, Rank As Integer) = Nothing
        Dim Releases As JArray
        Do
            Dim Json = NetRequestByClient($"{ReleasesApi}?per_page={PageSize}&page={Page}", Accept:="application/vnd.github+json",
                                         Timeout:=10000, RequireJson:=True, Encoding:=Encoding.UTF8)
            Releases = JArray.Parse(Json)
            For Each Release In Releases.OfType(Of JObject)()
                Dim Candidate = ParseReleaseVersion(Release, IncludeBetaReleases)
                If Candidate.Number IsNot Nothing AndAlso Candidate.CompareTo(Latest) > 0 Then Latest = Candidate
            Next
            Page += 1
        Loop While Releases.Count = PageSize
        Return Latest
    End Function

    Friend Function ParseReleaseVersion(Release As JObject, IncludeBeta As Boolean) As (Number As Version, Rank As Integer)
        Dim Tag = Release.Value(Of String)("tag_name")
        Dim IsPrerelease = Release.Value(Of Boolean?)("prerelease")
        If Release.Value(Of Boolean?)("draft").GetValueOrDefault(True) OrElse
           Not IsPrerelease.HasValue OrElse (IsPrerelease.Value AndAlso Not IncludeBeta) OrElse
           String.IsNullOrEmpty(Tag) OrElse Not Tag.StartsWith(ReleaseTagPrefix, StringComparison.Ordinal) Then Return Nothing

        Dim Parts = Tag.Substring(ReleaseTagPrefix.Length).Split("-"c)
        Dim Parsed As Version = Nothing
        If Not Version.TryParse(Parts(0), Parsed) OrElse Parsed.Revision < 0 OrElse Parts(0) <> Parsed.ToString() Then Return Nothing
        If Not IsPrerelease.Value Then
            If Parts.Length <> 1 Then Return Nothing
            Return (Parsed, Integer.MaxValue)
        End If
        Dim Beta As Integer
        If Parts.Length <> 2 OrElse Not Parts(1).StartsWith("beta", StringComparison.Ordinal) OrElse
           Not Integer.TryParse(Parts(1).Substring(4), Beta) OrElse Beta <= 0 OrElse Beta = Integer.MaxValue OrElse
           Parts(1) <> $"beta{Beta}" Then Return Nothing
        Return (Parsed, Beta)
    End Function

End Module
