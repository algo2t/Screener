﻿Imports System.IO
Imports System.Threading
Imports Algo2TradeBLL

Public Class CPRNarrowRangeStocks
    Inherits StockSelection

    Private ReadOnly _maximumRangePer As Decimal

    Public Sub New(ByVal canceller As CancellationTokenSource,
                   ByVal cmn As Common,
                   ByVal stockType As Integer,
                   ByVal minRangePer As Decimal)
        MyBase.New(canceller, cmn, stockType)
        _maximumRangePer = minRangePer
    End Sub

    Public Overrides Async Function GetStockDataAsync(ByVal startDate As Date, ByVal endDate As Date) As Task(Of DataTable)
        Await Task.Delay(0).ConfigureAwait(False)
        Dim ret As New DataTable
        ret.Columns.Add("Date")
        ret.Columns.Add("Trading Symbol")
        ret.Columns.Add("Lot Size")
        ret.Columns.Add("ATR %")
        ret.Columns.Add("Blank Candle %")
        ret.Columns.Add("Day ATR")
        ret.Columns.Add("Previous Day Open")
        ret.Columns.Add("Previous Day Low")
        ret.Columns.Add("Previous Day High")
        ret.Columns.Add("Previous Day Close")
        ret.Columns.Add("Slab")
        ret.Columns.Add("Range %")

        Using atrStock As New ATRStockSelection(_canceller)
            AddHandler atrStock.Heartbeat, AddressOf OnHeartbeat

            Dim tradingDate As Date = startDate
            While tradingDate <= endDate
                _bannedStockFileName = Path.Combine(My.Application.Info.DirectoryPath, String.Format("Bannned Stocks {0}.csv", tradingDate.ToString("ddMMyyyy")))
                For Each runningFile In Directory.GetFiles(My.Application.Info.DirectoryPath, "Bannned Stocks *.csv")
                    If Not runningFile.Contains(tradingDate.ToString("ddMMyyyy")) Then File.Delete(runningFile)
                Next
                Dim bannedStockList As List(Of String) = Nothing
                Using bannedStock As New BannedStockDataFetcher(_bannedStockFileName, _canceller)
                    AddHandler bannedStock.Heartbeat, AddressOf OnHeartbeat
                    bannedStockList = Await bannedStock.GetBannedStocksData(tradingDate).ConfigureAwait(False)
                End Using

                Dim atrStockList As Dictionary(Of String, InstrumentDetails) = Await atrStock.GetATRStockData(_eodTable, tradingDate, bannedStockList, False).ConfigureAwait(False)
                If atrStockList IsNot Nothing AndAlso atrStockList.Count > 0 Then
                    _canceller.Token.ThrowIfCancellationRequested()
                    Dim tempStockList As Dictionary(Of String, String()) = Nothing
                    For Each runningStock In atrStockList.Keys
                        _canceller.Token.ThrowIfCancellationRequested()
                        Dim eodPayload As Dictionary(Of Date, Payload) = _cmn.GetRawPayload(_eodTable, runningStock, tradingDate.AddDays(-20), tradingDate)
                        If eodPayload IsNot Nothing AndAlso eodPayload.Count > 0 Then
                            Dim lastDayPayload As Payload = eodPayload.LastOrDefault.Value
                            If lastDayPayload.PayloadDate.Date = tradingDate.Date Then
                                lastDayPayload = eodPayload.LastOrDefault.Value.PreviousCandlePayload
                            End If
                            If lastDayPayload IsNot Nothing Then
                                Dim pivot As Decimal = (lastDayPayload.High + lastDayPayload.Low + lastDayPayload.Close) / 3
                                Dim bc As Decimal = (lastDayPayload.High + lastDayPayload.Low) / 2
                                Dim tc As Decimal = (pivot - bc) + pivot
                                Dim currentDayRange As Decimal = Math.Abs(tc - bc)

                                Dim cprPayload As Dictionary(Of Date, PivotRange) = Nothing
                                Indicator.CentralPivotRange.CalculateCPR(eodPayload, cprPayload)
                                Dim sumOfPreviousRange As Decimal = 0
                                Dim counter As Integer = 0
                                For Each runningCPR In cprPayload.OrderByDescending(Function(x)
                                                                                        Return x.Key
                                                                                    End Function)
                                    If runningCPR.Key.Date <> tradingDate.Date Then
                                        Dim range As Decimal = Math.Abs(runningCPR.Value.TopCentralPivot - runningCPR.Value.BottomCentralPivot)
                                        sumOfPreviousRange += range

                                        counter += 1
                                        If counter = 5 Then Exit For
                                    End If
                                Next
                                Dim avgRange As Decimal = sumOfPreviousRange / 5
                                Dim rangePer As Decimal = Math.Round((currentDayRange / avgRange) * 100, 2)
                                If rangePer < _maximumRangePer Then
                                    If tempStockList Is Nothing Then tempStockList = New Dictionary(Of String, String())
                                    tempStockList.Add(runningStock, {rangePer})
                                End If
                            End If
                        End If
                    Next
                    If tempStockList IsNot Nothing AndAlso tempStockList.Count > 0 Then
                        Dim stockCounter As Integer = 0
                        For Each runningStock In tempStockList.OrderBy(Function(x)
                                                                           Return CDec(x.Value(0))
                                                                       End Function)
                            _canceller.Token.ThrowIfCancellationRequested()
                            Dim row As DataRow = ret.NewRow
                            row("Date") = tradingDate.ToString("dd-MM-yyyy")
                            row("Trading Symbol") = atrStockList(runningStock.Key).TradingSymbol
                            row("Lot Size") = atrStockList(runningStock.Key).LotSize
                            row("ATR %") = Math.Round(atrStockList(runningStock.Key).ATRPercentage, 4)
                            row("Blank Candle %") = atrStockList(runningStock.Key).BlankCandlePercentage
                            row("Day ATR") = Math.Round(atrStockList(runningStock.Key).DayATR, 4)
                            row("Previous Day Open") = atrStockList(runningStock.Key).PreviousDayOpen
                            row("Previous Day Low") = atrStockList(runningStock.Key).PreviousDayLow
                            row("Previous Day High") = atrStockList(runningStock.Key).PreviousDayHigh
                            row("Previous Day Close") = atrStockList(runningStock.Key).PreviousDayClose
                            row("Slab") = atrStockList(runningStock.Key).Slab
                            row("Range") = runningStock.Value(0)

                            ret.Rows.Add(row)
                            stockCounter += 1
                            If stockCounter = My.Settings.NumberOfStockPerDay Then Exit For
                        Next
                    End If
                End If

                tradingDate = tradingDate.AddDays(1)
            End While
        End Using
        Return ret
    End Function
End Class
