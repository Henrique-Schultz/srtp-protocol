# SRTP - Simple Reliable Transport Protocol

Implementacao da Parte 1 do trabalho de Laboratorio de Redes: um protocolo de transporte confiavel simples sobre UDP, usando Stop-and-Wait.

## Requisitos

- .NET 8 ou superior
- Windows, Linux ou macOS com suporte ao SDK do .NET

O projeto usa `net8.0` e foi validado com SDK .NET 9, que compila projetos .NET 8.

## Como compilar

```bash
dotnet build
```

## Como executar

### Receiver

```bash
dotnet run -- --listen --port 5000 --out recebido.bin --window 1 --mode saw
```

### Sender

```bash
dotnet run -- --host 127.0.0.1 --port 5000 --file entrada.bin --window 1 --mode saw
```

Se `--listen` estiver presente, o programa roda como receiver. Caso contrario, roda como sender.

## Argumentos

- `--listen`: ativa o modo receiver.
- `--host IP-ou-hostname`: endereco do receiver, usado no modo sender.
- `--port P`: porta base do protocolo. O receiver escuta em `P`; o sender usa localmente `P+1`.
- `--file caminho`: arquivo de entrada enviado pelo sender.
- `--out caminho`: arquivo de saida gravado pelo receiver.
- `--window N`: janela proposta no handshake, de 1 a 255. Nesta parte, o comportamento efetivo e Stop-and-Wait com janela 1.
- `--mode saw`: modo Stop-and-Wait. E o unico modo implementado nesta parte.

## Teste local

Em um terminal:

```bash
dotnet run -- --listen --port 5000 --out recebido.bin --mode saw
```

Em outro terminal:

```bash
dotnet run -- --host 127.0.0.1 --port 5000 --file entrada.bin --mode saw
```

Para gerar um arquivo de teste com mais de 50 pacotes:

```powershell
$bytes = New-Object byte[] 20000
[System.Random]::new().NextBytes($bytes)
[IO.File]::WriteAllBytes("entrada.bin", $bytes)
```

Verificacao por hash no PowerShell:

```powershell
Get-FileHash .\entrada.bin -Algorithm SHA256
Get-FileHash .\recebido.bin -Algorithm SHA256
```

Os hashes devem ser identicos.

## Protocolo

- Cabecalho fixo de 9 bytes.
- Flags `SYN`, `FIN`, `ACK` e `NACK`.
- Campos `SEQ` e `ACK` de 14 bits, com wrap-around em 16383.
- Payload maximo de 255 bytes.
- CRC32 IEEE calculado sobre cabecalho com CRC zerado mais payload.
- Three-way handshake: `SYN`, `SYN+ACK`, `ACK`.
- Transferencia confiavel por Stop-and-Wait.
- Timeout fixo de 100 ms para retransmissao.
- Encerramento com `FIN` e `FIN+ACK`.

## Limitacoes conhecidas

- Apenas o modo Stop-and-Wait (`saw`) esta implementado.
- Go-Back-N e Selective Repeat ficaram apenas previstos pela organizacao em camadas.
- O protocolo e half-duplex: em uma sessao, somente o sender envia dados de aplicacao.
- Pacotes com CRC invalido sao descartados silenciosamente, sem NACK.
