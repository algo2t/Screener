﻿Imports System.IO
Imports System.Threading
Imports Algo2TradeBLL

Public Class EODBTST_BullishEngulfingStocks
    Inherits StockSelection

    Public Sub New(ByVal canceller As CancellationTokenSource,
                   ByVal cmn As Common,
                   ByVal stockType As Integer)
        MyBase.New(canceller, cmn, stockType)
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
        ret.Columns.Add("Current Day Close")
        ret.Columns.Add("Slab")
        ret.Columns.Add("Supporting 1")
        ret.Columns.Add("Supporting 2")

        Using atrStock As New ATRStockSelection(_canceller)
            AddHandler atrStock.Heartbeat, AddressOf OnHeartbeat

            Dim tradingDate As Date = startDate
            While tradingDate <= endDate
                _bannedStockFileName = Path.Combine(My.Application.Info.DirectoryPath, String.Format("Bannned Stocks {0}.csv", tradingDate.ToString("ddMMyyyy")))
                For Each runningFile In Directory.GetFiles(My.Application.Info.DirectoryPath, "Bannned Stocks *.csv")
                    If Not runningFile.Contains(tradingDate.ToString("ddMMyyyy")) Then File.Delete(runningFile)
                Next
                Dim bannedStockList As List(Of String) = Nothing
                'Using bannedStock As New BannedStockDataFetcher(_bannedStockFileName, _canceller)
                '    AddHandler bannedStock.Heartbeat, AddressOf OnHeartbeat
                '    bannedStockList = Await bannedStock.GetBannedStocksData(tradingDate).ConfigureAwait(False)
                'End Using

                Dim atrStockList As Dictionary(Of String, InstrumentDetails) = Await atrStock.GetATRStockData(_eodTable, tradingDate, bannedStockList, False).ConfigureAwait(False)
                If atrStockList IsNot Nothing AndAlso atrStockList.Count > 0 Then
                    _canceller.Token.ThrowIfCancellationRequested()
                    Dim tempStockList As Dictionary(Of String, String()) = Nothing
                    For Each runningStock In atrStockList.Keys
                        Dim eodPayload As Dictionary(Of Date, Payload) = _cmn.GetRawPayload(Common.DataBaseTable.EOD_POSITIONAL, runningStock, tradingDate.AddDays(-400), tradingDate)
                        If eodPayload IsNot Nothing AndAlso eodPayload.Count > 200 AndAlso eodPayload.ContainsKey(tradingDate.Date) Then
                            Dim currentDayCandle As Payload = eodPayload(tradingDate.Date)
                            If currentDayCandle.CandleColor = Color.Green Then
                                Dim bufferRemarks As String = Nothing
                                If currentDayCandle.High > currentDayCandle.PreviousCandlePayload.High AndAlso
                                currentDayCandle.Low < currentDayCandle.PreviousCandlePayload.Low Then
                                    bufferRemarks = "Without Buffer"
                                Else
                                    Dim buffer As Decimal = CalculateBuffer(currentDayCandle.Close, Utilities.Numbers.NumberManipulation.RoundOfType.Floor)
                                    If currentDayCandle.High + buffer > currentDayCandle.PreviousCandlePayload.High AndAlso
                                    currentDayCandle.Low - buffer < currentDayCandle.PreviousCandlePayload.Low Then
                                        bufferRemarks = "With Buffer"
                                    End If
                                End If
                                If bufferRemarks IsNot Nothing Then
                                    Dim bodyRemark As String = Nothing
                                    If (currentDayCandle.CandleBody / currentDayCandle.CandleRange) * 100 >= 30 Then
                                        bodyRemark = "Greater Than 30%"
                                    End If

                                    If tempStockList Is Nothing Then tempStockList = New Dictionary(Of String, String())
                                    tempStockList.Add(runningStock, {bufferRemarks, bodyRemark})
                                End If
                            End If
                        End If
                    Next
                    If tempStockList IsNot Nothing AndAlso tempStockList.Count > 0 Then
                        Dim stockCounter As Integer = 0
                        For Each runningStock In tempStockList
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
                            row("Current Day Close") = atrStockList(runningStock.Key).CurrentDayClose
                            row("Slab") = atrStockList(runningStock.Key).Slab
                            row("Supporting 1") = runningStock.Value(0)
                            row("Supporting 2") = runningStock.Value(1)

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